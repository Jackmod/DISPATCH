using System.Text.Json.Serialization;

namespace Dispatch.Core.Profiles;

/// <summary>The four agencies LSPDFR ships with.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Agency>))]
public enum Agency
{
    /// <summary>Los Santos Police Department.</summary>
    Lspd,

    /// <summary>Los Santos Sheriff's Department.</summary>
    Lssd,

    /// <summary>San Andreas Highway Patrol.</summary>
    Sahp,

    /// <summary>Blaine County Sheriff's Office.</summary>
    Bcso,
}

/// <summary>
/// One officer: who you are on the radio and on the dash cam.
/// </summary>
/// <remarks>
/// Several of these values are written into mod config files rather than only
/// being displayed — the callsign reaches Callout Interface and Grammar Police,
/// the department name reaches Dash Cam V — so this is the single source that
/// keeps them agreeing with each other.
/// </remarks>
public sealed record OfficerProfile
{
    /// <summary>Stable identity, so a rename does not orphan the profile.</summary>
    public required Guid Id { get; init; }

    /// <summary>Officer name, as it appears on the dash cam and in dispatch.</summary>
    public required string Name { get; init; }

    /// <summary>Which agency.</summary>
    public Agency Agency { get; init; } = Agency.Lspd;

    /// <summary>Division number, 1 to 10.</summary>
    public int CallsignDivision { get; init; } = 1;

    /// <summary>LAPD phonetic word, for example <c>ADAM</c>.</summary>
    public string CallsignPhonetic { get; init; } = "ADAM";

    /// <summary>Beat number, 1 to 24.</summary>
    public int CallsignBeat { get; init; } = 7;

    /// <summary>Department name for the dash cam overlay.</summary>
    public string DepartmentName { get; init; } = "Los Santos Police Department";

    /// <summary>Air unit callsign, used by Heli Assistance.</summary>
    public string AirUnitCallsign { get; init; } = "AIR 1";

    /// <summary>Name of the control profile this officer uses.</summary>
    public string ControlProfile { get; init; } = "Suggested";

    /// <summary>The callsign as spoken and displayed, for example <c>1 ADAM 7</c>.</summary>
    [JsonIgnore]
    public string Callsign => $"{CallsignDivision} {CallsignPhonetic} {CallsignBeat}";

    /// <summary>
    /// The agency code mod configs expect. Grammar Police and Callout Interface
    /// both key off this rather than the display name.
    /// </summary>
    [JsonIgnore]
    public string AgencyCode => Agency switch
    {
        Agency.Lspd => "LSPD",
        Agency.Lssd => "LSSD",
        Agency.Sahp => "SAHP",
        Agency.Bcso => "BCSO",
        _ => "LSPD",
    };

    /// <summary>Creates a profile with a fresh identity.</summary>
    public static OfficerProfile Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
    };
}
