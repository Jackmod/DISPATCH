namespace Dispatch.Core.Configuration;

/// <summary>
/// Resolves a mod's config file on disk even when it did not land at the exact
/// relative path a catalogue predicted.
/// </summary>
/// <remarks>
/// The catalogues carry a best-guess relative path for each file, but mods
/// disagree about folder names and casing — Spotlight ships a folder literally
/// named "Spotlight Resources" while the hint reads "spotlight_resources", and
/// several plugins generate their ini a level deeper than expected. A rigid
/// <see cref="Path.Combine(string, string)"/> against the hint therefore misses,
/// and the write is silently dropped.
///
/// <para>
/// This walks the same mod folders the scanner does and finds the file by name,
/// so an edit lands in the real ini wherever it actually is. It is deliberately
/// conservative: the exact hinted path wins when it exists, a single name match
/// is taken, and an ambiguous name is only resolved when a candidate's folder
/// matches the hint's — never a blind guess that could edit the wrong mod's file.
/// </para>
/// </remarks>
public static class ModFileLocator
{
    // The same roots IniScanner walks, so "find it anywhere" means the same thing
    // in the writers as it does on the settings screen.
    private static readonly string[] Roots = ["plugins", "scripts", "lspdfr", "ragepluginhook"];

    /// <summary>
    /// Resolves a relative config-file hint to a real absolute path under the game
    /// folder, or null when nothing matches. Tries the exact hint first, then
    /// searches by file name (a glob is allowed) across the game root and the mod
    /// folders. An ambiguous name resolves only to a candidate whose parent folder
    /// matches the hint's; otherwise null, so a wrong file is never edited.
    /// </summary>
    /// <param name="gamePath">The game folder to search under.</param>
    /// <param name="relativeHint">The catalogue's relative path (may be a glob filename).</param>
    public static string? Resolve(string gamePath, string relativeHint)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(relativeHint))
        {
            return null;
        }

        var normalised = relativeHint.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        var pattern = Path.GetFileName(normalised);
        if (pattern.Length == 0)
        {
            return null;
        }

        var isGlob = pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal);

        // The exact hinted path wins whenever it is a concrete file that exists.
        if (!isGlob)
        {
            var exact = Path.Combine(gamePath, normalised);
            if (File.Exists(exact))
            {
                return exact;
            }
        }

        var matches = Search(gamePath, pattern)
            .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // Several files share the name. Only trust a match whose folder matches the
        // hint's folder (Spotlight Resources ~= spotlight_resources); otherwise leave
        // it unresolved rather than risk editing a different mod's like-named file.
        var hintFolder = Fold(LastSegment(Path.GetDirectoryName(normalised)));
        if (hintFolder.Length == 0)
        {
            return null;
        }

        return matches.FirstOrDefault(
            m => Fold(LastSegment(Path.GetDirectoryName(m))) == hintFolder);
    }

    /// <summary>
    /// Resolves a hint and returns its path relative to the game folder (forward
    /// slashes), or null when unresolved. Used to report where a write actually
    /// landed rather than where it was hinted.
    /// </summary>
    public static string? ResolveRelative(string gamePath, string relativeHint)
    {
        var full = Resolve(gamePath, relativeHint);
        return full is null ? null : Path.GetRelativePath(gamePath, full).Replace('\\', '/');
    }

    private static IEnumerable<string> Search(string gamePath, string pattern)
    {
        // Loose files in the game root (TrainerV.ini and friends), then everything
        // under the mod folders, recursively.
        foreach (var file in Safe(gamePath, pattern, SearchOption.TopDirectoryOnly))
        {
            yield return file;
        }

        foreach (var root in Roots)
        {
            var dir = Path.Combine(gamePath, root);
            if (Directory.Exists(dir))
            {
                foreach (var file in Safe(dir, pattern, SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> Safe(string directory, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, option);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            // A malformed glob in a hint should never take a write down.
            return [];
        }
    }

    private static string LastSegment(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        var trimmed = directory.Replace('\\', '/').TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    // Case, space, underscore and hyphen carry no meaning across these mods'
    // folder names, so fold them away before comparing.
    private static string Fold(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
