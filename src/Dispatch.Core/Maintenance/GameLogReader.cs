using System.Text.RegularExpressions;

namespace Dispatch.Core.Maintenance;

/// <summary>A problem found in a game log, translated into plain language.</summary>
/// <param name="Title">What failed, named.</param>
/// <param name="Explanation">Why, and what it means.</param>
/// <param name="FixCommand">A stable command id for a one-click fix, if one exists.</param>
public sealed record LogFinding(string Title, string Explanation, string? FixCommand = null);

/// <summary>
/// Parses RagePluginHook.log and LSPDFR.log, which hold the answer to almost
/// every problem in language nobody can read.
/// </summary>
/// <remarks>
/// A pattern map turns the log's own phrasing into an explanation and a fix. The
/// map is deliberately data — a list of regex-to-message rules — so a new
/// failure mode is a new entry rather than new code, matching the spec's intent
/// that it be refreshable without a rebuild.
/// </remarks>
public sealed partial class GameLogReader
{
    private sealed record Pattern(Regex Match, Func<Match, LogFinding> Build);

    private static readonly IReadOnlyList<Pattern> Patterns = BuildPatterns();

    /// <summary>Reads a log's text and returns every problem it recognises.</summary>
    public IReadOnlyList<LogFinding> Translate(string logText)
    {
        ArgumentNullException.ThrowIfNull(logText);

        var findings = new List<LogFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in logText.Split('\n'))
        {
            foreach (var pattern in Patterns)
            {
                var match = pattern.Match.Match(line);
                if (match.Success)
                {
                    var finding = pattern.Build(match);

                    // The same failure often repeats across a log; report each
                    // distinct problem once.
                    if (seen.Add(finding.Title))
                    {
                        findings.Add(finding);
                    }
                }
            }
        }

        return findings;
    }

    private static IReadOnlyList<Pattern> BuildPatterns() =>
    [
        new Pattern(
            MissingDependencyRegex(),
            m => new LogFinding(
                $"{m.Groups["plugin"].Value.Trim()} failed to load",
                $"It needs {m.Groups["dependency"].Value.Trim()}, which is not in your game folder.",
                FixCommand: "install-dependency")),

        new Pattern(
            BuildMismatchRegex(),
            _ => new LogFinding(
                "Script Hook V does not match your game",
                "Script Hook V is built for a different game build than the one installed, so nothing "
                + "loads. This is behind almost every 'LSPDFR is broken' report.",
                FixCommand: "open-shv-page")),

        new Pattern(
            UnhandledExceptionRegex(),
            m => new LogFinding(
                $"{m.Groups["plugin"].Value.Trim()} crashed",
                "A plugin threw an unhandled exception while loading. Safe mode will confirm whether it "
                + "is the cause.",
                FixCommand: "safe-mode")),

        new Pattern(
            MissingAssemblyRegex(),
            m => new LogFinding(
                $"A required file is missing: {m.Groups["assembly"].Value.Trim()}",
                "A plugin could not find a file it depends on. It was either never installed or removed "
                + "afterward, often by antivirus.",
                FixCommand: "reinstall-from-cache")),
    ];

    // "[ERROR] GrammarPolice could not be loaded because RageNativeUI is missing"
    [GeneratedRegex(
        @"(?<plugin>[\w\s\.]+?)\s+(?:could not be loaded|failed to load).*?(?<dependency>[\w\.]+)\s+(?:is missing|not found)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MissingDependencyRegex();

    [GeneratedRegex(
        @"(?:wrong version|incompatible|unsupported game version|version mismatch).*?script\s*hook",
        RegexOptions.IgnoreCase)]
    private static partial Regex BuildMismatchRegex();

    [GeneratedRegex(
        @"unhandled exception.*?in\s+(?<plugin>[\w\s\.]+?)(?:\s|$|\.dll)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnhandledExceptionRegex();

    [GeneratedRegex(
        @"could not load file or assembly\s+'?(?<assembly>[\w\.]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MissingAssemblyRegex();
}
