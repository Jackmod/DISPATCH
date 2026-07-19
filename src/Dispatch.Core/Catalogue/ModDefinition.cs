namespace Dispatch.Core.Catalogue;

/// <summary>How a mod is fetched.</summary>
public enum SourceKind
{
    /// <summary>A GitHub releases API endpoint. Zero interaction.</summary>
    GitHubRelease,

    /// <summary>A plain static download link.</summary>
    DirectHttp,

    /// <summary>A page the embedded browser drives.</summary>
    Browser,
}

/// <summary>Whether a mod's compatibility tracks the game build or the LSPDFR API.</summary>
public enum CompatibilityAnchor
{
    /// <summary>Locked to the exact GTA V build. Script Hook V and friends.</summary>
    GameBuild,

    /// <summary>Depends on the LSPDFR/RPH API, not the game build. Most plugins.</summary>
    LspdfrApi,
}

/// <summary>
/// One mod: where it comes from, where its files go, and what it depends on.
/// </summary>
/// <remarks>
/// This is the row the whole install engine reads. Everything the spec's
/// install-engine reference table specifies is a field here, so the catalogue
/// is data and the runner is a loop over it.
/// </remarks>
public sealed record ModDefinition
{
    /// <summary>Stable identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Author, shown on the mod card.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>How it is fetched.</summary>
    public SourceKind Source { get; init; }

    /// <summary>The page or endpoint it is fetched from.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>For a GitHub source: the <c>owner/repo</c> and an asset name pattern.</summary>
    public string? Repo { get; init; }

    /// <summary>For a GitHub source: a regex the release asset name must match.</summary>
    public string? AssetPattern { get; init; }

    /// <summary>Where its files go.</summary>
    public required PlacementRule Placement { get; init; }

    /// <summary>What its version compatibility tracks.</summary>
    public CompatibilityAnchor Anchor { get; init; } = CompatibilityAnchor.LspdfrApi;

    /// <summary>Mod ids this one needs placed before it.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Sort weight for install order. Lower installs first.
    /// </summary>
    /// <remarks>
    /// The spec's order is core first, then Callout Interface, then everything
    /// else, with Search Items Reborn and Ultimate Backup last because they
    /// deliberately replace Stop The Ped files and must win. Expressed as a
    /// weight rather than a hand-sorted list so a new mod slots in by number.
    /// </remarks>
    public int Order { get; init; } = 100;
}
