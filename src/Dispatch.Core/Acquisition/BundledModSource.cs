using Dispatch.Core.Catalogue;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// The mod pack roots, searched in order. A distinct type rather than a bare
/// list so the container can inject it without an <c>IEnumerable&lt;string&gt;</c>
/// ambiguity, and so <see cref="BundledModSource"/> registers as a concrete type
/// that <c>TryAddEnumerable</c> can tell apart from the network sources.
/// </summary>
/// <param name="Roots">Pack roots, most specific first.</param>
public sealed record ModPackRoots(IReadOnlyList<string> Roots);

/// <summary>
/// Resolves a mod's archive from a local mod pack rather than the network.
/// </summary>
/// <remarks>
/// This is how the mods that cannot be fetched automatically — LSPDFR itself,
/// Script Hook V, and every lcpdfr/gta5-mods plugin behind a download flow no
/// program can drive — still install unattended. They are downloaded once by
/// hand, dropped into the pack, and shipped with the app; from then on the
/// installer only has to unpack and place them.
///
/// <para>
/// The pack is a flat dump: archives go into the pack folders (or its preset
/// subfolders) in whatever names they downloaded as, and
/// <see cref="ModArchiveMatcher"/> works out which archive is which mod by name.
/// No renaming, no per-mod folders. Several roots are searched — a user pack
/// under LOCALAPPDATA first, then the pack shipped beside the executable — and
/// the match is recomputed whenever the set of archives changes.
/// </para>
///
/// <para>
/// Registered ahead of the network sources, so a mod present in the pack installs
/// from it even if it also has a GitHub release. The version-locked core mods are
/// simply left out of the pack, so nothing matches them here and they fall through
/// to <see cref="GitHubReleaseSource"/> to be fetched fresh.
/// </para>
/// </remarks>
public sealed class BundledModSource : IDownloadSource
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    private readonly IReadOnlyList<string> _roots;
    private readonly ILogger<BundledModSource> _logger;
    private readonly object _gate = new();

    private string? _signature;
    private IReadOnlyDictionary<string, ModArchiveMatcher.Assignment> _matches =
        new Dictionary<string, ModArchiveMatcher.Assignment>();

    /// <summary>Constructs the source over the configured pack roots.</summary>
    public BundledModSource(ModPackRoots roots, ILogger<BundledModSource> logger)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(logger);

        // Distinct, non-empty, order preserved: the first root that has the mod
        // wins, so a user pack listed before the shipped pack overrides it.
        _roots = roots.Roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger = logger;
    }

    /// <inheritdoc />
    public SourceKind Kind => SourceKind.Browser;

    /// <inheritdoc />
    public bool CanHandle(ModDefinition mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return CurrentMatches().ContainsKey(mod.Id);
    }

    /// <inheritdoc />
    public Task<DownloadResult> DownloadAsync(
        ModDefinition mod,
        string destinationDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (!CurrentMatches().TryGetValue(mod.Id, out var match))
        {
            throw new AcquisitionException(mod.Id, "no archive for this mod was found in the mod pack.");
        }

        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, Path.GetFileName(match.ArchivePath));

        // Copy rather than move: the pack is the source of truth and must survive
        // for the next run and for a repair. Report the copy as a single progress
        // step so the UI shows the same shape as a network fetch.
        var length = new FileInfo(match.ArchivePath).Length;
        File.Copy(match.ArchivePath, destination, overwrite: true);
        progress?.Report(new DownloadProgress(length, length));

        _logger.LogInformation(
            "Placed {Mod} from mod pack: {Archive} (matched on '{Key}')",
            mod.Id, Path.GetFileName(match.ArchivePath), match.MatchedKey);

        return Task.FromResult(new DownloadResult(destination, VersionFromName(match.ArchivePath), $"modpack:{match.ArchivePath}"));
    }

    /// <summary>
    /// The current archive-to-mod matches, recomputed only when the set of
    /// archives on disk has changed since the last look.
    /// </summary>
    /// <remarks>
    /// Memoised behind a signature of the archive files so a run that asks about
    /// thirty mods does not rescan and rematch thirty times, while a user who
    /// drops a new archive mid-session still sees it picked up on the next install.
    /// </remarks>
    private IReadOnlyDictionary<string, ModArchiveMatcher.Assignment> CurrentMatches()
    {
        var archives = EnumerateArchives();
        var signature = Signature(archives);

        lock (_gate)
        {
            if (signature != _signature)
            {
                var result = ModArchiveMatcher.Match(archives, ModCatalogue.Mods.Values);
                _matches = result.Matches;
                _signature = signature;

                if (result.UnmatchedArchives.Count > 0)
                {
                    _logger.LogInformation(
                        "Mod pack: {Matched} archive(s) matched, {Unmatched} not recognised ({Files})",
                        result.Matches.Count,
                        result.UnmatchedArchives.Count,
                        string.Join(", ", result.UnmatchedArchives.Select(Path.GetFileName)));
                }
            }

            return _matches;
        }
    }

    /// <summary>Every archive under every pack root, recursively.</summary>
    private List<string> EnumerateArchives()
    {
        var archives = new List<string>();

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            archives.AddRange(Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsArchive));
        }

        return archives;
    }

    private static string Signature(IEnumerable<string> archives) =>
        string.Join(
            "|",
            archives
                .Select(a => new FileInfo(a))
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(f => $"{f.FullName}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));

    private static bool IsArchive(string path) =>
        !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
        && ArchiveExtensions.Any(e => string.Equals(e, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pulls a version out of an archive file name when one is obviously present,
    /// e.g. "ELS V1.05.rar" → "V1.05". Best-effort, for the log only.
    /// </summary>
    private static string? VersionFromName(string archivePath)
    {
        var name = Path.GetFileNameWithoutExtension(archivePath);
        var match = System.Text.RegularExpressions.Regex.Match(
            name, @"[vV]?\d+(\.\d+)+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }
}
