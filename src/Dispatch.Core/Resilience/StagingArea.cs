using Dispatch.Core.Infrastructure;

namespace Dispatch.Core.Resilience;

/// <summary>
/// Scratch space for extraction, deliberately outside the game folder.
/// </summary>
/// <remarks>
/// Nothing is ever extracted into the game directory. Everything goes to
/// <c>%TEMP%/Dispatch/staging/&lt;run-id&gt;/&lt;mod&gt;/</c>, is validated
/// there, and only then are files moved into place one at a time. This is what
/// makes a corrupt archive, a mod that restructured its zip, or a crash
/// mid-extract unable to leave garbage in the game folder — the worst case is a
/// mess in a temp directory that gets purged.
///
/// <para>
/// Path-traversal defence lives here rather than in the extractor, so every
/// caller gets it: <see cref="ResolveWithin"/> refuses any member path that
/// resolves outside its mod's staging folder, which is the one archive attack
/// that can write anywhere on the disk.
/// </para>
/// </remarks>
public sealed class StagingArea
{
    /// <summary>Creates a staging area for a run.</summary>
    /// <param name="stagingRoot">The staging root, under %TEMP%.</param>
    /// <param name="runId">This run's identifier.</param>
    public StagingArea(string stagingRoot, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        Root = Path.Combine(stagingRoot, runId);
    }

    /// <summary>This run's staging root.</summary>
    public string Root { get; }

    /// <summary>
    /// Returns a fresh, empty folder for a mod, deleting any partial extraction
    /// left from a previous attempt.
    /// </summary>
    /// <remarks>
    /// Always fresh: a partial extraction from a crashed run is never trusted,
    /// because a half-unzipped archive can be missing exactly the file a resume
    /// would then place. Deleting and starting over is the only safe option.
    /// </remarks>
    public string PrepareModDirectory(string mod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mod);

        var dir = Path.Combine(Root, Sanitize(mod));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Resolves an archive member path within a base directory, refusing
    /// anything that escapes it.
    /// </summary>
    /// <returns>The absolute destination path, guaranteed inside <paramref name="baseDir"/>.</returns>
    /// <exception cref="UnsafeArchivePathException">The member escapes the base directory.</exception>
    public static string ResolveWithin(string baseDir, string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberPath);

        var fullBase = Path.GetFullPath(baseDir);

        // A rooted member — a drive letter, a UNC path, or a leading slash that
        // roots to the current drive on Windows — is refused outright. A
        // well-formed mod archive never contains one, and combining it with the
        // base can silently discard the base entirely.
        var normalised = memberPath.Replace('\\', '/');
        if (Path.IsPathRooted(memberPath) || Path.IsPathRooted(normalised))
        {
            throw new UnsafeArchivePathException(memberPath);
        }

        var cleaned = normalised.TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(fullBase, cleaned));

        var baseWithSep = fullBase.EndsWith(Path.DirectorySeparatorChar)
            ? fullBase
            : fullBase + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, fullBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafeArchivePathException(memberPath);
        }

        return combined;
    }

    /// <summary>Deletes this run's staging tree. Called on success; kept on failure for diagnosis.</summary>
    public void Purge()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>Hashes a staged file, so placement can be verified against it.</summary>
    public static Task<string> HashAsync(string path, CancellationToken cancellationToken = default) =>
        Hashing.Sha256Async(path, cancellationToken);

    private static string Sanitize(string mod)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(mod.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}

/// <summary>Thrown when an archive member path resolves outside its target directory.</summary>
public sealed class UnsafeArchivePathException(string memberPath)
    : Exception($"Archive member '{memberPath}' resolves outside the extraction target and was rejected.")
{
    /// <summary>The offending member path.</summary>
    public string MemberPath { get; } = memberPath;
}
