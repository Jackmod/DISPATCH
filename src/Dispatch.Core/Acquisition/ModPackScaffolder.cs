using System.Text;
using Dispatch.Core.Catalogue;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// Creates and inspects the mod pack skeleton: one labelled folder per mod that
/// has to be supplied by hand, ready for its archive to be dropped in.
/// </summary>
/// <remarks>
/// Driven entirely by the catalogue, so the skeleton never drifts from the mods
/// the installer actually knows about. A mod that GitHub can fetch on its own is
/// deliberately left out — it needs no folder, because it is downloaded fresh at
/// install time and bundling a stale copy would only fight the version lock.
/// </remarks>
public static class ModPackScaffolder
{
    /// <summary>Whether a mod is fetched automatically and so needs no pack folder.</summary>
    public static bool IsAutoFetched(ModDefinition mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return mod.Source == SourceKind.GitHubRelease && !string.IsNullOrWhiteSpace(mod.Repo);
    }

    /// <summary>Every mod that must be supplied through the pack, in install order.</summary>
    public static IReadOnlyList<ModDefinition> ModsNeedingPack() =>
        ModCatalogue.Mods.Values
            .Where(m => !IsAutoFetched(m))
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>One of the three drop folders and the mods a user should fill it with.</summary>
    /// <param name="PresetId">The catalogue preset it maps to.</param>
    /// <param name="Folder">The folder on disk.</param>
    /// <param name="Mods">The pack-supplied mods that belong in it.</param>
    public sealed record DropFolder(string PresetId, string Folder, IReadOnlyList<ModDefinition> Mods);

    /// <summary>
    /// Creates the three drop folders — one per preset — under
    /// <paramref name="root"/>, each with a README listing the mods to download
    /// into it. Archives are matched by name at install time, so a user drops them
    /// in unsorted and nothing needs renaming.
    /// </summary>
    /// <returns>The folders created, for reporting.</returns>
    public static IReadOnlyList<DropFolder> Scaffold(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Directory.CreateDirectory(root);
        WriteRootReadme(root);

        var folders = new List<DropFolder>();

        foreach (var (presetId, folderName) in FolderNames)
        {
            var folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            var mods = ModCatalogue.ModsFor(presetId)
                .Where(m => !IsAutoFetched(m))
                .ToList();

            WriteFolderReadme(folder, folderName, mods);
            folders.Add(new DropFolder(presetId, folder, mods));
        }

        return folders;
    }

    /// <summary>The three folders, mapping a friendly name to its preset.</summary>
    private static readonly (string PresetId, string FolderName)[] FolderNames =
    [
        ("standard", "1 - Normal"),
        ("full-duty", "2 - Plugins"),
        ("realism", "3 - Realism"),
    ];

    private static void WriteFolderReadme(string folder, string folderName, IReadOnlyList<ModDefinition> mods)
    {
        var text = new StringBuilder()
            .AppendLine($"DISPATCH MOD PACK — {folderName}")
            .AppendLine()
            .AppendLine("Download each mod below and drop its archive (.zip, .rar or .7z) straight into")
            .AppendLine("this folder. Do NOT rename or unzip them — Dispatch matches each archive to its")
            .AppendLine("mod by name and unpacks only what the user selects.")
            .AppendLine();

        if (mods.Count == 0)
        {
            text.AppendLine("(No mods defined for this setup yet.)");
        }
        else
        {
            foreach (var mod in mods)
            {
                text.AppendLine($"  - {mod.Name}   ({(string.IsNullOrWhiteSpace(mod.Author) ? "—" : mod.Author)})");
            }
        }

        File.WriteAllText(Path.Combine(folder, "_WHAT-GOES-HERE.txt"), text.ToString());
    }

    private static void WriteRootReadme(string root)
    {
        var auto = ModCatalogue.Mods.Values.Where(IsAutoFetched).OrderBy(m => m.Order).ToList();

        var readme = new StringBuilder()
            .AppendLine("DISPATCH MOD PACK")
            .AppendLine("=================")
            .AppendLine()
            .AppendLine("Three folders, one per setup. Download the mods for a setup and drop every")
            .AppendLine("archive straight into its folder — unsorted, original names, still zipped.")
            .AppendLine("Dispatch works out which archive is which mod by its file name.")
            .AppendLine()
            .AppendLine("  1 - Normal     LSPDFR and its essentials")
            .AppendLine("  2 - Plugins    the full plugin lineup")
            .AppendLine("  3 - Realism    the realism layer (defined later)")
            .AppendLine()
            .AppendLine("A mod shared by more than one setup only needs to be dropped in ONCE, in any")
            .AppendLine("folder — the match looks across all three.")
            .AppendLine()
            .AppendLine("At install time only the mods the user ticks are unpacked; the rest of the")
            .AppendLine("pack is never touched, so no unused files reach the game folder.")
            .AppendLine()
            .AppendLine("Fetched automatically from GitHub — do NOT put these in the pack (they are")
            .AppendLine("version-locked and pulled fresh to match the game build):")
            .AppendLine();

        foreach (var mod in auto)
        {
            readme.AppendLine($"  - {mod.Name}  ({mod.Repo})");
        }

        File.WriteAllText(Path.Combine(root, "README.txt"), readme.ToString());
    }
}
