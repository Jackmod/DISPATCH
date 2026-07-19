namespace Dispatch.Core.Catalogue;

/// <summary>How a mod's archive contents map onto the game folder.</summary>
public enum PlacementKind
{
    /// <summary>Copy the whole archive to the game root.</summary>
    RootAll,

    /// <summary>Copy only named files to a destination folder.</summary>
    NamedFiles,

    /// <summary>Copy the contents of a named folder inside the archive to a destination.</summary>
    FolderContents,

    /// <summary>Copy a single named file to a destination folder.</summary>
    SingleFile,
}

/// <summary>
/// One rule describing where a mod's files go, as a data row.
/// </summary>
/// <remarks>
/// A declarative record rather than a branch in the installer, exactly as the
/// spec requires: adding a mod later is a new row, never new code. The two hard
/// rules the spec calls out — never overwrite Callout Interface's assembly once
/// it is installed, and strip the bundled RageNativeUI from the archives that
/// carry a stale copy — are expressed as <see cref="NeverOverwrite"/> and
/// <see cref="StripBeforeExtract"/> flags on the rows that need them, so they
/// travel with the data and cannot be forgotten in a rewrite.
/// </remarks>
/// <param name="Kind">How the archive maps onto the folder.</param>
/// <param name="Destination">Where files land, relative to the game root. Empty for the root.</param>
/// <param name="SourceFolder">The folder inside the archive to take from, for <see cref="PlacementKind.FolderContents"/>.</param>
/// <param name="Files">The named files to take, for <see cref="PlacementKind.NamedFiles"/> and <see cref="PlacementKind.SingleFile"/>.</param>
/// <param name="Exclude">Files to leave behind even when copying everything.</param>
/// <param name="StripBeforeExtract">Files to delete from the staged archive before placing anything, so a bundled stale copy never wins.</param>
public sealed record PlacementRule(
    PlacementKind Kind,
    string Destination = "",
    string? SourceFolder = null,
    IReadOnlyList<string>? Files = null,
    IReadOnlyList<string>? Exclude = null,
    IReadOnlyList<string>? StripBeforeExtract = null);

/// <summary>
/// Files that, once present, are never overwritten by a later mod.
/// </summary>
/// <remarks>
/// Grammar Police and LIAR both ship their own copies of these, and overwriting
/// the copy Callout Interface placed silently breaks Callout Interface — a
/// failure with no error, just a feature that stops working. The installer
/// consults this set before every write.
/// </remarks>
public static class ProtectedAssemblies
{
    /// <summary>The assemblies no later mod may overwrite.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CalloutInterface.ApplicationExtension.dll",
        "IPTCommon.dll",
    };

    /// <summary>True when a destination filename must never be overwritten.</summary>
    public static bool IsProtected(string fileName) => Names.Contains(Path.GetFileName(fileName));
}
