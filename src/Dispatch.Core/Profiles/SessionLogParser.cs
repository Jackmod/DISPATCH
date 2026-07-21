using System.Globalization;
using System.Text.RegularExpressions;

namespace Dispatch.Core.Profiles;

/// <summary>
/// Turns an <c>LSPDFR.log</c> into one recorded shift: how long it ran, and a
/// best-effort count of callouts, arrests, pursuits and citations.
/// </summary>
/// <remarks>
/// The log is the only record of what happened on a shift, but its wording is not
/// a stable contract, so this is deliberately conservative: the duration comes from
/// the timestamps LSPDFR prefixes to every line — which are reliable — while the
/// event counts match broad phrases and are documented as best-effort. When a phrase
/// is not recognised the count stays at zero; the parser undercounts rather than
/// invents a number, because a wrong stat is worse than a modest one.
///
/// <para>
/// A session is only produced when the log shows the officer actually went on duty.
/// An idle log — the game opened and closed without a shift — returns null so a
/// zero-minute "session" is never recorded.
/// </para>
/// </remarks>
public sealed partial class SessionLogParser
{
    /// <summary>
    /// Parses one shift out of an LSPDFR log. <paramref name="endedAt"/> should be the
    /// log file's last-write time, which both dates the shift and lets the caller tell
    /// an already-recorded log from a fresh one.
    /// </summary>
    /// <returns>The shift, or null when the log shows no on-duty time.</returns>
    public SessionStat? Parse(string logText, DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(logText);

        // No sign of a shift — nothing to record.
        if (!OnDutyRegex().IsMatch(logText))
        {
            return null;
        }

        var minutes = DurationMinutes(logText);

        var callouts = Count(logText, CalloutRegex());
        var arrests = Count(logText, ArrestRegex());
        var pursuits = Count(logText, PursuitRegex());
        var citations = Count(logText, CitationRegex());

        return new SessionStat(endedAt, minutes, callouts, arrests, pursuits, citations);
    }

    /// <summary>
    /// Minutes between the first and last timestamped line. LSPDFR stamps lines with
    /// a time of day only, so a shift that runs past midnight is wrapped forward a day
    /// rather than read as negative.
    /// </summary>
    private static double DurationMinutes(string logText)
    {
        TimeSpan? first = null;
        TimeSpan? last = null;

        foreach (Match match in TimestampRegex().Matches(logText))
        {
            if (!TimeSpan.TryParseExact(
                    match.Groups["t"].Value,
                    [@"hh\:mm\:ss\.fff", @"hh\:mm\:ss"],
                    CultureInfo.InvariantCulture,
                    out var time))
            {
                continue;
            }

            first ??= time;
            last = time;
        }

        if (first is not { } start || last is not { } end)
        {
            return 0;
        }

        var span = end - start;
        if (span < TimeSpan.Zero)
        {
            span += TimeSpan.FromDays(1);
        }

        return Math.Round(span.TotalMinutes, 1);
    }

    /// <summary>Counts the lines matching a pattern, one per line at most.</summary>
    private static int Count(string logText, Regex pattern)
    {
        var count = 0;
        foreach (var line in logText.Split('\n'))
        {
            if (pattern.IsMatch(line))
            {
                count++;
            }
        }

        return count;
    }

    // Leading "[19:32:15.123]" or "[19:32:15]" — LSPDFR's per-line time stamp.
    [GeneratedRegex(@"\[(?<t>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"going on duty|went on duty|now on duty", RegexOptions.IgnoreCase)]
    private static partial Regex OnDutyRegex();

    // Broad enough to survive small wording changes, narrow enough not to match a
    // menu label. Either order, since LSPDFR logs "Starting callout" but other lines
    // read "callout accepted".
    [GeneratedRegex(@"callout.*(start|begin|request|accept|display)|(start|begin|request|accept|display).*callout", RegexOptions.IgnoreCase)]
    private static partial Regex CalloutRegex();

    [GeneratedRegex(@"(has been arrested|under arrest|arrest.*suspect|suspect.*arrest)", RegexOptions.IgnoreCase)]
    private static partial Regex ArrestRegex();

    [GeneratedRegex(@"pursuit.*(start|begin|initiat)|(start|begin|initiat).*pursuit", RegexOptions.IgnoreCase)]
    private static partial Regex PursuitRegex();

    [GeneratedRegex(@"citation|ticket issued|issued a ticket", RegexOptions.IgnoreCase)]
    private static partial Regex CitationRegex();
}
