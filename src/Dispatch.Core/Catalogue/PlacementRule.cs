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

    /// <summary>
    /// Work it out: strip any wrapper folders, then merge the recognised game
    /// folders (plugins, scripts, lspdfr, x64…) and loose root files into the
    /// game folder. Handles the long tail of LSPDFR mods that all follow the same
    /// "extract into the game folder" convention without a bespoke rule each.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Cannot be auto-installed — an OpenIV package (<c>.oiv</c>), an add-on DLC
    /// (<c>dlc.rpf</c> + a dlclist edit) or a loose <c>.ymap</c> needing a map
    /// loader. The whole mod is copied into the OpenIV import folder, laid out as
    /// shipped, for the user to apply through OpenIV themselves. Nothing is placed
    /// in the game folder.
    /// </summary>
    ManualImport,
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
///
/// <para>
/// Both the historical names and the current ones are listed: newer Callout
/// Interface renamed <c>CalloutInterface.ApplicationExtension.dll</c> to
/// <c>CalloutInterfaceAPI.dll</c> and <c>IPTCommon.dll</c> to <c>IPT.Common.dll</c>,
/// and Grammar Police and LIAR ship the new names — so those are the ones that must
/// actually be protected today.
/// </para>
/// </remarks>
public static class ProtectedAssemblies
{
    /// <summary>The assemblies no later mod may overwrite.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Current names (Callout Interface 1.4+, shipped by Grammar Police and LIAR).
        "CalloutInterfaceAPI.dll",
        "IPT.Common.dll",
        // Historical names, kept so older archives are still handled.
        "CalloutInterface.ApplicationExtension.dll",
        "IPTCommon.dll",
    };

    /// <summary>True when a destination filename must never be overwritten.</summary>
    public static bool IsProtected(string fileName) => Names.Contains(Path.GetFileName(fileName));
}
