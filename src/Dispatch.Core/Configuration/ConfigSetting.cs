namespace Dispatch.Core.Configuration;

/// <summary>How a setting name is matched to the keys in a config file.</summary>
public enum ConfigMatch
{
    /// <summary>One key whose normalised name equals the setting's.</summary>
    Exact,

    /// <summary>
    /// Every key whose normalised name contains the setting's — for the guide's
    /// "all Use Natives flags" and "all Auto Tab entries" style instructions,
    /// where one line stands in for a whole block of keys.
    /// </summary>
    Contains,
}

/// <summary>One value to write into a config file, named as the guide names it.</summary>
/// <param name="Name">
/// The human setting name from the guide (e.g. "Open Computer Key"). Matched to
/// the file's real key by normalising both — stripping case, spaces and
/// punctuation — so the exact key spelling in the file does not have to be known.
/// </param>
/// <param name="Value">
/// The value to set. May contain officer placeholders: <c>{callsign}</c>,
/// <c>{officer}</c>, <c>{department}</c>, <c>{airunit}</c>.
/// </param>
/// <param name="Match">Whether one key or every matching key is written.</param>
/// <param name="Section">
/// When set, only keys in this ini section are considered — for files that reuse a
/// key name across sections (Spotlight's <c>Toggle</c> under <c>[Keyboard]</c>,
/// <c>[Controller]</c> and <c>[Mouse]</c>). Null matches in any section.
/// </param>
public sealed record ConfigSetting(string Name, string Value, ConfigMatch Match = ConfigMatch.Exact, string? Section = null);

/// <summary>The officer details the config values are personalised with.</summary>
/// <param name="Callsign">The officer's callsign, e.g. "1 ADAM 7".</param>
/// <param name="OfficerName">The officer's name.</param>
/// <param name="Department">The department name, for dash cam and similar.</param>
/// <param name="AirUnitCallsign">The air-unit callsign, for Heli Assistance.</param>
public sealed record OfficerValues(
    string Callsign,
    string OfficerName,
    string Department,
    string AirUnitCallsign)
{
    /// <summary>Reasonable defaults so a config run never writes an empty placeholder.</summary>
    public static OfficerValues Default { get; } = new("1 ADAM 7", "Officer", "San Andreas State Police", "AIR 1");

    /// <summary>Replaces the officer placeholders in a value.</summary>
    public string Fill(string value) => value
        .Replace("{callsign}", Callsign, StringComparison.OrdinalIgnoreCase)
        .Replace("{officer}", OfficerName, StringComparison.OrdinalIgnoreCase)
        .Replace("{department}", Department, StringComparison.OrdinalIgnoreCase)
        .Replace("{airunit}", AirUnitCallsign, StringComparison.OrdinalIgnoreCase);
}
