using System.Text.Json.Serialization;

namespace Dispatch.Core.Profiles;

/// <summary>One completed session, as parsed from the game logs.</summary>
/// <param name="EndedAt">When the session ended.</param>
/// <param name="Minutes">How long it ran.</param>
/// <param name="Callouts">Callouts taken.</param>
/// <param name="Arrests">Arrests made.</param>
/// <param name="Pursuits">Pursuits.</param>
/// <param name="Citations">Citations written.</param>
public sealed record SessionStat(
    DateTimeOffset EndedAt,
    double Minutes,
    int Callouts,
    int Arrests,
    int Pursuits,
    int Citations);

/// <summary>
/// The career record behind the profile screen: who the officer is over time,
/// not just right now.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="DispatchProfile"/> because it grows without bound —
/// a row per session — and has a different write rhythm: the profile changes when
/// the user edits their identity, this changes after every shift. Totals are
/// derived rather than stored so a session added or corrected can never leave a
/// running total wrong.
/// </remarks>
public sealed record ProfileStats
{
    /// <summary>The version this code writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the file this came from.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Path to the chosen profile picture, if any.</summary>
    public string? AvatarPath { get; init; }

    /// <summary>When this officer first reported for duty — the "on the force since" date.</summary>
    public DateTimeOffset? FirstSeen { get; init; }

    /// <summary>Every recorded session, oldest first.</summary>
    public IReadOnlyList<SessionStat> Sessions { get; init; } = [];

    /// <summary>Total time on duty, in minutes.</summary>
    [JsonIgnore]
    public double TotalMinutes => Sessions.Sum(s => s.Minutes);

    /// <summary>Total time on duty, in whole hours.</summary>
    [JsonIgnore]
    public int TotalHours => (int)(TotalMinutes / 60);

    /// <summary>Number of shifts worked.</summary>
    [JsonIgnore]
    public int SessionCount => Sessions.Count;

    /// <summary>Career callouts.</summary>
    [JsonIgnore]
    public int TotalCallouts => Sessions.Sum(s => s.Callouts);

    /// <summary>Career arrests.</summary>
    [JsonIgnore]
    public int TotalArrests => Sessions.Sum(s => s.Arrests);

    /// <summary>Career pursuits.</summary>
    [JsonIgnore]
    public int TotalPursuits => Sessions.Sum(s => s.Pursuits);

    /// <summary>Career citations.</summary>
    [JsonIgnore]
    public int TotalCitations => Sessions.Sum(s => s.Citations);

    /// <summary>The most recent session, if any.</summary>
    [JsonIgnore]
    public SessionStat? LastSession =>
        Sessions.Count == 0 ? null : Sessions.OrderByDescending(s => s.EndedAt).First();

    /// <summary>Average shift length in minutes, or zero with no sessions.</summary>
    [JsonIgnore]
    public double AverageSessionMinutes => SessionCount == 0 ? 0 : TotalMinutes / SessionCount;

    /// <summary>Whole days since the officer first reported, or zero when unknown.</summary>
    public int DaysOnForce(DateTimeOffset now) =>
        FirstSeen is { } first ? Math.Max(0, (int)(now - first).TotalDays) : 0;
}
