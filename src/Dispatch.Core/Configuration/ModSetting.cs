using Dispatch.Core.Profiles;

namespace Dispatch.Core.Configuration;

/// <summary>How a setting is edited, which decides the control the UI shows for it.</summary>
public enum SettingKind
{
    /// <summary>An on/off switch. The file stores it as a pair of literals that differ per mod.</summary>
    Toggle,

    /// <summary>A number within a range: a position, a scale, a threshold, a duration.</summary>
    Number,

    /// <summary>One of a fixed set of named options.</summary>
    Choice,

    /// <summary>Free text: a callsign, a department name.</summary>
    Text,
}

/// <summary>An officer field a setting defaults to, so identity stays in one place.</summary>
public enum ProfileField
{
    /// <summary>Not derived from the officer.</summary>
    None,

    /// <summary>The spoken callsign, e.g. <c>1 ADAM 7</c>.</summary>
    Callsign,

    /// <summary>The callsign in upper case, as some MDT fields want it.</summary>
    CallsignUpper,

    /// <summary>The department name for the dash cam overlay.</summary>
    DepartmentName,

    /// <summary>The officer name.</summary>
    OfficerName,

    /// <summary>The air unit callsign.</summary>
    AirUnitCallsign,

    /// <summary>The agency code, e.g. <c>LSPD</c>.</summary>
    AgencyCode,
}

/// <summary>One named choice for a <see cref="SettingKind.Choice"/> setting.</summary>
/// <param name="Label">What the user reads.</param>
/// <param name="Value">What is written to the file.</param>
public sealed record SettingOption(string Label, string Value);

/// <summary>
/// One editable line in a mod config file, described well enough that the UI can
/// show it as a switch, a slider or a dropdown instead of a raw ini value.
/// </summary>
/// <remarks>
/// This is the settings equivalent of a <see cref="Controls.GameAction"/>: a
/// declarative row saying where the value lives, what it means, and what shape it
/// is. The screen renders the table; adding a mod's setting later is a new row,
/// never new code. The value that reaches the file is always the mod's own
/// literal — <c>YES</c>/<c>NO</c>, <c>True</c>/<c>False</c>, <c>true</c>/<c>false</c>
/// — because these files disagree and rewriting one in another's style is the kind
/// of quiet change that breaks a mod without saying so.
/// </remarks>
public sealed record ModSetting
{
    private static readonly HashSet<string> Truthy =
        new(StringComparer.OrdinalIgnoreCase) { "true", "yes", "on", "1", "enabled" };

    /// <summary>Stable identifier, unique across all mods.</summary>
    public required string Id { get; init; }

    /// <summary>What it does, in plain words.</summary>
    public required string Name { get; init; }

    /// <summary>A sentence explaining it, shown under the name and in search.</summary>
    public required string Description { get; init; }

    /// <summary>Which mod owns it.</summary>
    public required string Plugin { get; init; }

    /// <summary>Grouping within a plugin, for the section headers.</summary>
    public string Category { get; init; } = "General";

    /// <summary>The config file, relative to the game folder.</summary>
    public required string ConfigFile { get; init; }

    /// <summary>The key within that file.</summary>
    public required string ConfigKey { get; init; }

    /// <summary>
    /// The section the key lives under, when it matters. Empty means match the key
    /// wherever it appears — which is how the curated settings work, since they use
    /// keys unique within their file. A scan records the real section so a file with
    /// the same key in two sections edits the right one.
    /// </summary>
    public string Section { get; init; } = string.Empty;

    /// <summary>
    /// True when this setting was found by scanning a config file rather than being
    /// in the curated catalogue. Discovered settings carry heuristic names and
    /// types; curated ones carry hand-written descriptions and options.
    /// </summary>
    public bool Discovered { get; init; }

    /// <summary>What kind of value it is.</summary>
    public SettingKind Kind { get; init; } = SettingKind.Text;

    /// <summary>The value used when the file has none yet.</summary>
    public string Default { get; init; } = string.Empty;

    /// <summary>For a toggle: the literal written when on.</summary>
    public string OnLiteral { get; init; } = "true";

    /// <summary>For a toggle: the literal written when off.</summary>
    public string OffLiteral { get; init; } = "false";

    /// <summary>For a number: the lowest allowed value.</summary>
    public double Min { get; init; }

    /// <summary>For a number: the highest allowed value.</summary>
    public double Max { get; init; } = 100;

    /// <summary>For a number: the step between values.</summary>
    public double Step { get; init; } = 1;

    /// <summary>For a number: a unit shown beside the field, e.g. <c>mph</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>For a choice: the named options.</summary>
    public IReadOnlyList<SettingOption> Options { get; init; } = [];

    /// <summary>For text: whether the value is wrapped in quotes in the file.</summary>
    public bool Quoted { get; init; }

    /// <summary>The officer field this defaults to, if any.</summary>
    public ProfileField Profile { get; init; } = ProfileField.None;

    /// <summary>Reads a raw file value as a bool, tolerant of every truthy spelling.</summary>
    public bool ParseBool(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && Truthy.Contains(Unquote(raw).Trim());

    /// <summary>Formats a bool into this setting's own on/off literal.</summary>
    public string BoolToRaw(bool value) => value ? OnLiteral : OffLiteral;

    /// <summary>The label of the option a raw value selects, or the raw value itself.</summary>
    public string ChoiceLabel(string? raw)
    {
        var value = Unquote(raw ?? string.Empty).Trim();
        return Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))?.Label
            ?? value;
    }

    /// <summary>Strips surrounding quotes, whichever kind, from a stored value.</summary>
    public static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>Resolves this setting's default for a given officer.</summary>
    public string DefaultFor(OfficerProfile? officer)
    {
        if (officer is null || Profile == ProfileField.None)
        {
            return Default;
        }

        return Profile switch
        {
            ProfileField.Callsign => officer.Callsign,
            ProfileField.CallsignUpper => officer.Callsign.ToUpperInvariant(),
            ProfileField.DepartmentName => officer.DepartmentName,
            ProfileField.OfficerName => officer.Name,
            ProfileField.AirUnitCallsign => officer.AirUnitCallsign,
            ProfileField.AgencyCode => officer.AgencyCode,
            _ => Default,
        };
    }
}
