namespace Dispatch.Core.Palette;

/// <summary>What a palette entry does when chosen.</summary>
public enum PaletteAction
{
    /// <summary>Navigate to a section.</summary>
    Navigate,

    /// <summary>Jump to an action's keybinding.</summary>
    EditBinding,

    /// <summary>Run a one-off command.</summary>
    Run,
}

/// <summary>One thing the palette can find and do.</summary>
/// <param name="Title">What is shown, and what is matched against.</param>
/// <param name="Subtitle">Context shown beneath, also matched.</param>
/// <param name="Group">Section heading in the results.</param>
/// <param name="Action">What choosing it does.</param>
/// <param name="Target">The section key, binding id, or command name.</param>
public sealed record PaletteEntry(
    string Title,
    string Subtitle,
    string Group,
    PaletteAction Action,
    string Target);

/// <summary>
/// Fuzzy search across everything the palette can reach.
/// </summary>
/// <remarks>
/// The ranking is a subsequence match, not a substring one. Typing <c>arst</c>
/// should find "Arrest suspect", and typing <c>clngta</c> should find "Clean
/// GTA folder" — a command palette that only matches contiguous text is barely
/// faster than scrolling. Contiguous and prefix matches still rank first, so
/// the exact thing you typed does not lose to a scattered coincidence.
/// </remarks>
public static class CommandPalette
{
    /// <summary>Finds and ranks entries against a query.</summary>
    /// <param name="query">What the user typed. Empty returns everything, ungrouped by rank.</param>
    /// <param name="entries">Everything searchable.</param>
    /// <param name="limit">How many results to return.</param>
    public static IReadOnlyList<PaletteEntry> Search(
        string query,
        IEnumerable<PaletteEntry> entries,
        int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (string.IsNullOrWhiteSpace(query))
        {
            return entries.Take(limit).ToList();
        }

        var term = query.Trim();

        return entries
            .Select(entry => (Entry: entry, Score: Score(term, entry)))
            .Where(scored => scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(scored => scored.Entry)
            .ToList();
    }

    /// <summary>Scores an entry against a query. Zero means no match.</summary>
    /// <remarks>
    /// The title is weighted above the subtitle, so "Controls" the section
    /// beats a mod whose description happens to mention controls. A match on a
    /// word boundary scores higher than one buried mid-word, which is what
    /// keeps the obvious result on top.
    /// </remarks>
    internal static int Score(string term, PaletteEntry entry)
    {
        var titleScore = ScoreField(term, entry.Title);
        var subtitleScore = ScoreField(term, entry.Subtitle);

        if (titleScore == 0 && subtitleScore == 0)
        {
            return 0;
        }

        // Title matches are worth far more than subtitle matches.
        return (titleScore * 4) + subtitleScore;
    }

    private static int ScoreField(string term, string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return 0;
        }

        // Exact and prefix beat everything, so typing the whole word wins.
        if (field.Equals(term, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (field.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 500;
        }

        var contiguous = field.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (contiguous >= 0)
        {
            // A contiguous hit at a word boundary is worth more than one
            // buried inside a word.
            var atBoundary = contiguous == 0 || field[contiguous - 1] is ' ' or '-' or '/';
            return atBoundary ? 300 : 150;
        }

        return SubsequenceScore(term, field);
    }

    /// <summary>
    /// Scores a scattered subsequence match, rewarding hits that fall on word
    /// boundaries.
    /// </summary>
    private static int SubsequenceScore(string term, string field)
    {
        var t = 0;
        var score = 0;
        var previousMatchAt = -2;

        for (var f = 0; f < field.Length && t < term.Length; f++)
        {
            if (char.ToLowerInvariant(field[f]) != char.ToLowerInvariant(term[t]))
            {
                continue;
            }

            var atBoundary = f == 0 || field[f - 1] is ' ' or '-' or '/';
            score += atBoundary ? 15 : 5;

            // Consecutive matched characters read as a real fragment rather
            // than a coincidence, so they score extra.
            if (f == previousMatchAt + 1)
            {
                score += 5;
            }

            previousMatchAt = f;
            t++;
        }

        // Only a complete subsequence counts; a partial match is no match.
        return t == term.Length ? score : 0;
    }
}
