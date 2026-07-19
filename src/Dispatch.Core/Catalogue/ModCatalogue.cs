namespace Dispatch.Core.Catalogue;

/// <summary>One installable setup.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="ModIds">The mods it installs, by id.</param>
/// <param name="ComingSoon">Whether it is a placeholder tier.</param>
public sealed record Preset(string Id, string Name, IReadOnlyList<string> ModIds, bool ComingSoon = false);

/// <summary>
/// The mods Dispatch knows how to install, and the presets that group them.
/// </summary>
/// <remarks>
/// The declarative table the spec's install-engine reference describes. Every
/// destination quirk — Spotlight's resources folder, Heli Assistance landing in
/// <c>plugins/lspdfr</c>, ELS taking only what is under its Installation Files
/// tree — is a row here, and the runner is a loop that reads it. Adding a mod is
/// a new entry, never a new branch.
/// </remarks>
public static class ModCatalogue
{
    /// <summary>Every mod, keyed by id.</summary>
    public static readonly IReadOnlyDictionary<string, ModDefinition> Mods = Build();

    /// <summary>The three shipped presets.</summary>
    public static readonly IReadOnlyList<Preset> Presets =
    [
        new Preset("standard", "Standard Issue",
        [
            "scripthookv", "scripthookvdotnet", "ragenativeui", "lemonui", "lspdfr",
        ]),
        new Preset("full-duty", "Full Duty", FullDutyMods()),
        new Preset("realism", "Realism", [], ComingSoon: true),
    ];

    /// <summary>The mods in a preset, in install order.</summary>
    public static IReadOnlyList<ModDefinition> ModsFor(string presetId)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset is null)
        {
            return [];
        }

        return preset.ModIds
            .Where(Mods.ContainsKey)
            .Select(id => Mods[id])
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> FullDutyMods() =>
    [
        // Core, first.
        "scripthookv", "scripthookvdotnet", "ragenativeui", "lemonui", "lspdfr",
        "simpletrainer",
        // Callout Interface, before everything that ships a copy of its files.
        "calloutinterface",
        // Everything else.
        "stoptheped", "grammarpolice", "compulite", "heliassistance", "spotlight",
        "dashcamv", "els", "betterelsreflections", "customenvlighting", "ambienteffects",
        "fastdraw", "ingamescreenshot", "simplehud", "clearthewayv", "radiorealism",
        "immersiveeffects", "baitcar", "restrain", "liar", "skincontrol", "clipboard",
        // Deliberately last: they replace Stop The Ped files and must win.
        "searchitemsreborn", "ultimatebackup",
    ];

    private static IReadOnlyDictionary<string, ModDefinition> Build()
    {
        var mods = new List<ModDefinition>
        {
            // ===== Core (game-build locked) ===============================
            Github("scripthookv", "Script Hook V", "Alexander Blade",
                new PlacementRule(PlacementKind.NamedFiles, Files: ["dinput8.dll", "ScriptHookV.dll"], SourceFolder: "bin"),
                anchor: CompatibilityAnchor.GameBuild, order: 0),

            Github("scripthookvdotnet", "Script Hook V .NET", "crosire",
                new PlacementRule(PlacementKind.NamedFiles,
                    Files: ["ScriptHookVDotNet.asi", "ScriptHookVDotNet3.dll", "ScriptHookVDotNet2.dll"]),
                repo: "scripthookvdotnet/scripthookvdotnet", anchor: CompatibilityAnchor.GameBuild, order: 1),

            Github("ragenativeui", "RAGENativeUI", "alexguirre",
                new PlacementRule(PlacementKind.SingleFile, Files: ["RAGENativeUI.dll"]),
                repo: "alexguirre/RAGENativeUI", order: 2),

            Github("lemonui", "LemonUI", "justalemon",
                new PlacementRule(PlacementKind.SingleFile, Destination: "scripts", Files: ["LemonUI.SHVDN3.dll"]),
                repo: "justalemon/LemonUI", order: 3),

            Browser("lspdfr", "LSPD First Response", "G17 Media",
                new PlacementRule(PlacementKind.RootAll, Exclude: ["License.txt", "ReadMe - RagePluginHook.txt"]),
                order: 4),

            Browser("simpletrainer", "Simple Trainer", "sjaak327",
                new PlacementRule(PlacementKind.NamedFiles, Files: ["TrainerV.asi", "TrainerV.ini"]),
                order: 5),

            // ===== Callout Interface, before its copiers ==================
            Browser("calloutinterface", "Callout Interface", "Ryst",
                new PlacementRule(PlacementKind.SingleFile, Destination: "plugins", Files: ["CalloutInterface.dll"]),
                order: 10),

            // ===== Everything else ========================================
            Browser("stoptheped", "Stop The Ped", "Nifty",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            // Grammar Police and LIAR both bundle RageNativeUI; the root copy
            // wins, so strip theirs before extracting.
            Browser("grammarpolice", "Grammar Police", "TheDeadRiser",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins",
                    StripBeforeExtract: ["RageNativeUI.dll", "RAGENativeUI.dll"]), order: 40),

            Browser("compulite", "Charges & Citations", "Compulite",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins/lspdfr/Compulite"), order: 40),

            Browser("heliassistance", "Heli Assistance", "PNWParksFan",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins/lspdfr"), order: 40),

            Browser("spotlight", "Spotlight", "Yard1",
                new PlacementRule(PlacementKind.NamedFiles, Destination: "plugins",
                    Files: ["spotlight.dll", "spotlight_resources"]), order: 40),

            Browser("dashcamv", "Dash Cam V", "MrKrumpNasty",
                new PlacementRule(PlacementKind.SingleFile, Destination: "plugins", Files: ["DashCamV.dll"]), order: 40),

            Browser("els", "ELS", "Lt.Caine",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "Installation Files/Grand Theft Auto 5"),
                order: 40),

            Browser("betterelsreflections", "Better ELS Reflections", "Community",
                new PlacementRule(PlacementKind.SingleFile, Files: ["els.ini"],
                    SourceFolder: "Brighter with Higher Range and Brighter Takedowns"),
                dependsOn: ["els"], order: 41),

            Script("customenvlighting", "Custom Env Lighting"),
            Script("ambienteffects", "Ambient Effects"),
            Script("fastdraw", "Fast Draw"),
            Script("ingamescreenshot", "In-Game Screenshot"),
            Script("simplehud", "Simple HUD"),

            Browser("clearthewayv", "Clear The Way V", "Albo1125",
                new PlacementRule(PlacementKind.SingleFile, Destination: "plugins", Files: ["ClearTheWayV.dll"]), order: 40),

            Browser("radiorealism", "Radio Realism Alpha", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "lspdfr/audio/scanner", SourceFolder: "resident"),
                order: 40),

            Browser("immersiveeffects", "Immersive Effects", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            Browser("baitcar", "Bait Car", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            Browser("restrain", "Restrain", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            Browser("liar", "LIAR", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins",
                    StripBeforeExtract: ["RageNativeUI.dll", "RAGENativeUI.dll"]), order: 40),

            Browser("skincontrol", "Skin Control", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            Browser("clipboard", "Clipboard", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 40),

            // ===== Last: they replace Stop The Ped files ==================
            Browser("searchitemsreborn", "Search Items Reborn", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 90),

            Browser("ultimatebackup", "Ultimate Backup", "Albo1125",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "plugins"), order: 90),
        };

        return mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    private static ModDefinition Github(
        string id, string name, string author, PlacementRule placement,
        string? repo = null, CompatibilityAnchor anchor = CompatibilityAnchor.LspdfrApi, int order = 100) =>
        new()
        {
            Id = id,
            Name = name,
            Author = author,
            Source = SourceKind.GitHubRelease,
            Repo = repo,
            Placement = placement,
            Anchor = anchor,
            Order = order,
        };

    private static ModDefinition Browser(
        string id, string name, string author, PlacementRule placement,
        IReadOnlyList<string>? dependsOn = null, int order = 100) =>
        new()
        {
            Id = id,
            Name = name,
            Author = author,
            Source = SourceKind.Browser,
            Placement = placement,
            DependsOn = dependsOn ?? [],
            Order = order,
        };

    private static ModDefinition Script(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Author = "Community",
            Source = SourceKind.Browser,
            Placement = new PlacementRule(PlacementKind.FolderContents, Destination: "scripts"),
            Order = 40,
        };
}
