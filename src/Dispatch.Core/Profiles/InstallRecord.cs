using System.Text.Json.Serialization;

namespace Dispatch.Core.Profiles;

/// <summary>One file an install placed, and the hash it was placed with.</summary>
/// <param name="RelativePath">Path relative to the game folder.</param>
/// <param name="Sha256">Hash at placement time.</param>
/// <param name="Mod">Which mod placed it.</param>
public sealed record PlacedFile(string RelativePath, string Sha256, string Mod);

/// <summary>
/// What an install left behind: the plugin list, the game build it was made
/// against, and a hash of every file placed.
/// </summary>
/// <remarks>
/// This record is what lets the launcher tell three states apart that look the
/// same from a bare directory listing: not installed, installed and intact, and
/// installed-then-broken-underneath. A file that is now missing or reverted to
/// stock, checked against its recorded hash, is the fingerprint of a launcher
/// verification having wiped the mods — the most common way a working setup
/// dies, and one the user never connects to the cause.
/// </remarks>
public sealed record InstallRecord
{
    /// <summary>The version this code writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the file this came from.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>When the install ran.</summary>
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The GTA V build at install time.</summary>
    public string? GameBuild { get; init; }

    /// <summary>The preset that was installed.</summary>
    public string PresetId { get; init; } = string.Empty;

    /// <summary>The mods installed, by id.</summary>
    public IReadOnlyList<string> ModIds { get; init; } = [];

    /// <summary>Every file placed, with its hash.</summary>
    public IReadOnlyList<PlacedFile> Files { get; init; } = [];

    /// <summary>True when there is a record to check against.</summary>
    [JsonIgnore]
    public bool IsInstalled => Files.Count > 0;
}
