using System.Text.Json.Serialization;

namespace Dispatch.Core.Profiles;

/// <summary>
/// Everything Dispatch remembers about a user, as written to
/// <c>profile.json</c>.
/// </summary>
/// <remarks>
/// Carries a <see cref="SchemaVersion"/> from the first release rather than
/// from the first time it is needed. Adding versioning to a format already in
/// the wild means guessing what unversioned files meant, and this file holds
/// the only copy of a setup someone spent fifteen minutes building.
/// </remarks>
public sealed record DispatchProfile
{
    /// <summary>The version this code writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the file this was loaded from.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Every officer, in creation order.</summary>
    public IReadOnlyList<OfficerProfile> Officers { get; init; } = [];

    /// <summary>Which officer is on duty. Null when none has been created.</summary>
    public Guid? ActiveOfficerId { get; init; }

    /// <summary>Path to the chosen GTA V installation.</summary>
    public string? GamePath { get; init; }

    /// <summary>Appearance and behaviour settings.</summary>
    public AppearanceSettings Appearance { get; init; } = new();

    /// <summary>When this file was last written.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The officer currently on duty, if any.</summary>
    [JsonIgnore]
    public OfficerProfile? ActiveOfficer =>
        ActiveOfficerId is { } id ? Officers.FirstOrDefault(o => o.Id == id) : null;

    /// <summary>True when the first-run wizard has been completed.</summary>
    /// <remarks>
    /// Having an officer is the signal that first-run is done, so a returning user
    /// lands in the launcher — not back in the wizard — even if the game path did
    /// not persist. The launcher handles a missing game path itself (it prompts to
    /// set one), which is a far better place to fix it from than the setup flow.
    /// </remarks>
    [JsonIgnore]
    public bool IsConfigured => Officers.Count > 0;

    /// <summary>Returns a copy with <paramref name="officer"/> added or replaced, and made active.</summary>
    public DispatchProfile WithOfficer(OfficerProfile officer)
    {
        ArgumentNullException.ThrowIfNull(officer);

        var officers = Officers.Where(o => o.Id != officer.Id).Append(officer).ToList();

        return this with
        {
            Officers = officers,
            ActiveOfficerId = officer.Id,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Returns a copy without the given officer, reassigning the active one if needed.</summary>
    public DispatchProfile WithoutOfficer(Guid id)
    {
        var officers = Officers.Where(o => o.Id != id).ToList();

        return this with
        {
            Officers = officers,

            // Removing the officer on duty promotes whoever is left rather
            // than leaving a dangling identifier behind.
            ActiveOfficerId = ActiveOfficerId == id ? officers.FirstOrDefault()?.Id : ActiveOfficerId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>Appearance and behaviour preferences.</summary>
public sealed record AppearanceSettings
{
    /// <summary>Collapses every animation duration to zero and skips the intro.</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Restrained interaction sounds. Off by default.</summary>
    public bool SoundEnabled { get; init; }

    /// <summary>Discord Rich Presence. Off by default.</summary>
    public bool DiscordPresence { get; init; }

    /// <summary>Whether the first-run tour has been shown.</summary>
    public bool TourCompleted { get; init; }
}
