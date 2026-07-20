using System.Text.RegularExpressions;
using Dispatch.Core.Catalogue;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// Matches a folder of dumped archive files to the mods they belong to, by name,
/// so the pack needs no per-mod folders — everything goes in one place and this
/// works out which archive is which.
/// </summary>
/// <remarks>
/// Every mod contributes a set of keys — its id, its display name, and any
/// aliases — each reduced to a comparison form with version numbers, separators
/// and boilerplate words ("gta5", "final", "the") stripped. An archive matches a
/// mod when one of the mod's keys appears in the archive's reduced file name.
///
/// <para>
/// The score is the length of the matching key: a longer, more specific key beats
/// a short one, so "Ultimate Backup.rar" goes to Ultimate Backup rather than to a
/// mod whose key happens to be the substring "back". Assignment is greedy by
/// score, and each archive and each mod is used at most once, so the strongest,
/// least ambiguous pairings are locked in first.
/// </para>
///
/// <para>
/// It is deliberately conservative: an archive it cannot confidently place is
/// left unmatched rather than forced onto a weak candidate, because a wrong mod
/// placed into a game folder is far worse than one reported as still needed.
/// </para>
/// </remarks>
public static class ModArchiveMatcher
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    // Shortest key length allowed to match, to keep one- and two-letter ids from
    // latching onto unrelated file names. Three is deliberate: "els" is a real mod
    // id, and the greedy longest-key-wins rule keeps it from stealing an archive a
    // more specific key (e.g. "betterelsreflections") should own.
    private const int MinKeyLength = 3;

    /// <summary>One archive assigned to one mod, with the key and score that won it.</summary>
    /// <param name="ModId">The matched mod.</param>
    /// <param name="ArchivePath">The archive assigned to it.</param>
    /// <param name="MatchedKey">The mod key that matched.</param>
    /// <param name="Score">Match strength — the matched key's length.</param>
    public sealed record Assignment(string ModId, string ArchivePath, string MatchedKey, int Score);

    /// <summary>The full outcome of matching a set of archives to a set of mods.</summary>
    /// <param name="Matches">Mod id → the archive assigned to it.</param>
    /// <param name="UnmatchedArchives">Archives that fit no mod confidently.</param>
    public sealed record Result(
        IReadOnlyDictionary<string, Assignment> Matches,
        IReadOnlyList<string> UnmatchedArchives);

    /// <summary>
    /// Matches archives to mods. Each mod and each archive is used at most once,
    /// strongest pairings first.
    /// </summary>
    public static Result Match(IEnumerable<string> archivePaths, IEnumerable<ModDefinition> mods)
    {
        ArgumentNullException.ThrowIfNull(archivePaths);
        ArgumentNullException.ThrowIfNull(mods);

        var archives = archivePaths.Where(IsArchive).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var modList = mods.ToList();

        // Every candidate pairing that has any key match, strongest first.
        var candidates = new List<Assignment>();
        foreach (var mod in modList)
        {
            var keys = KeysFor(mod);
            foreach (var archive in archives)
            {
                var stem = Reduce(Path.GetFileNameWithoutExtension(archive));

                var best = keys
                    .Where(k => k.Length >= MinKeyLength && stem.Contains(k, StringComparison.Ordinal))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (best is not null)
                {
                    candidates.Add(new Assignment(mod.Id, archive, best, best.Length));
                }
            }
        }

        var matches = new Dictionary<string, Assignment>(StringComparer.Ordinal);
        var usedArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates
                     .OrderByDescending(c => c.Score)
                     .ThenBy(c => c.ModId, StringComparer.Ordinal))
        {
            if (matches.ContainsKey(candidate.ModId) || usedArchives.Contains(candidate.ArchivePath))
            {
                continue;
            }

            matches[candidate.ModId] = candidate;
            usedArchives.Add(candidate.ArchivePath);
        }

        var unmatched = archives.Where(a => !usedArchives.Contains(a)).ToList();
        return new Result(matches, unmatched);
    }

    /// <summary>The comparison keys for a mod: id, name and aliases, reduced.</summary>
    private static IReadOnlyList<string> KeysFor(ModDefinition mod)
    {
        var keys = new List<string> { Reduce(mod.Id), Reduce(mod.Name) };
        keys.AddRange(mod.Aliases.Select(Reduce));

        return keys
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reduces a name to its comparison form: lower case, no version numbers, no
    /// boilerplate words, no separators — just the letters and digits that
    /// identify the mod.
    /// </summary>
    public static string Reduce(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.ToLowerInvariant();

        // Version tokens: v1.05, 1.0.3570.1, _2_2, "b3258".
        text = Regex.Replace(text, @"\bv?\d+([._]\d+)+\b", " ");
        text = Regex.Replace(text, @"\bb?\d{3,}\b", " ");

        // Boilerplate that appears across many mod downloads and identifies none.
        // Deliberately excludes short glue words like "the" and "and": those can
        // sit glued inside an id ("stoptheped") but appear as separate words in a
        // file name ("Stop The Ped"), and stripping them would make the two reduce
        // differently and never match.
        text = Regex.Replace(
            text,
            @"\b(gta5?|gtav|grandtheftauto|mod|mods|final|release|setup|install|installer|version|plugin|plugins|x64)\b",
            " ",
            RegexOptions.CultureInvariant);

        // Everything else that is not a letter or digit falls away, leaving a
        // compact token to test for containment.
        return Regex.Replace(text, "[^a-z0-9]+", string.Empty);
    }

    private static bool IsArchive(string path) =>
        ArchiveExtensions.Any(e => string.Equals(e, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
        && !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);
}
