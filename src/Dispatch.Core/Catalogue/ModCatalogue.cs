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
        new Preset("realism", "Realism", RealismMods()),
    ];

    /// <summary>Every mod marked as a core dependency, in install order.</summary>
    public static readonly IReadOnlyList<ModDefinition> Required =
        Mods.Values.Where(m => m.Required).OrderBy(m => m.Order).ToList();

    /// <summary>
    /// The mods in a preset, in install order — always including the required
    /// core, so a preset can never resolve to a set that is missing a dependency
    /// the rest need to load.
    /// </summary>
    public static IReadOnlyList<ModDefinition> ModsFor(string presetId)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);

        // A preset with no mods of its own is a placeholder (Realism, until its
        // lineup is defined) — it installs nothing, not even the required core,
        // so it stays visibly empty.
        if (preset is null || preset.ModIds.Count == 0)
        {
            return [];
        }

        var ids = preset.ModIds.Concat(Required.Select(m => m.Id));

        return ids
            .Where(Mods.ContainsKey)
            .Select(id => Mods[id])
            .DistinctBy(m => m.Id)
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> FullDutyMods() =>
    [
        // Core, first.
        "scripthookv", "scripthookvdotnet", "ragenativeui", "lemonui", "lspdfr",
        "simpletrainer", "resourceadjuster",
        // Callout Interface, before everything that ships a copy of its files.
        "calloutinterface", "mdttextures",
        // Everything else from the guide.
        "stoptheped", "grammarpolice", "compulite", "charges", "heliassistance", "spotlight",
        "els", "betterelsreflections",
        "fastdraw", "simplehud", "clearthewayv", "radiorealism", "radiorealismfr",
        "immersiveeffects", "restrain", "liar", "clipboard",
        "speedradarlite", "richspoliceenhancement", "custompullover", "deadlyweapons",
        "riskiertrafficstops", "keepthedooropen", "stickywheels",
        // Deliberately last: they replace Stop The Ped files and must win.
        "searchitemsreborn", "ultimatebackup",
    ];

    /// <summary>Realism is everything in Full Duty plus the extended realism layer.</summary>
    private static IReadOnlyList<string> RealismMods() =>
        FullDutyMods().Concat(RealismExtras()).ToList();

    private static IReadOnlyList<string> RealismExtras() =>
    [
        "686callouts", "jobcenterv", "aet", "calloutlauncher", "calloutresponse",
        "chilllscallouts", "kucheracallouts", "mccallouts", "policingredefined", "r57",
        "realtrafficai", "resistplus", "modernsirenpack", "newsheli", "simpledeath",
        "superevents", "suspectsense", "tacsimscallouts", "trafficyield", "vhud",
        "fulltraffic", "frontend", "homeownership", "openworldinteriors", "holstersounds",
        // Newer plugins with bespoke layouts.
        "xtremetool", "simmycarlots", "spgr",
        // World, map & visual mods — set aside to the OpenIV import folder, not installed.
        "worldofvariety", "dnxchiliadtown", "dnxsenoraroad", "dnxcalafiaroads",
        "dnxpaletohighway", "dnxchiliadstateroads", "dnxbraddockpass",
        "gtavremastered", "ero", "improvementsingore",
    ];

    private static IReadOnlyDictionary<string, ModDefinition> Build()
    {
        var mods = new List<ModDefinition>
        {
            // ===== Core (game-build locked) ===============================
            Github("scripthookv", "Script Hook V", "Alexander Blade",
                new PlacementRule(PlacementKind.NamedFiles, Files: ["dinput8.dll", "ScriptHookV.dll"], SourceFolder: "bin"),
                anchor: CompatibilityAnchor.GameBuild, order: 0, required: true),

            Github("scripthookvdotnet", "Script Hook V .NET", "crosire",
                new PlacementRule(PlacementKind.NamedFiles,
                    Files: ["ScriptHookVDotNet.asi", "ScriptHookVDotNet3.dll", "ScriptHookVDotNet2.dll"]),
                repo: "scripthookvdotnet/scripthookvdotnet", anchor: CompatibilityAnchor.GameBuild, order: 1,
                required: true),

            Github("ragenativeui", "RAGENativeUI", "alexguirre",
                new PlacementRule(PlacementKind.SingleFile, Files: ["RAGENativeUI.dll"]),
                repo: "alexguirre/RAGENativeUI", order: 2, required: true, aliases: ["RativeUI"]),

            Github("lemonui", "LemonUI", "justalemon",
                new PlacementRule(PlacementKind.SingleFile, Destination: "scripts", Files: ["LemonUI.SHVDN3.dll"]),
                repo: "justalemon/LemonUI", order: 3),

            Browser("lspdfr", "LSPD First Response", "G17 Media",
                new PlacementRule(PlacementKind.RootAll, Exclude: ["License.txt", "ReadMe - RagePluginHook.txt"]),
                order: 4, required: true),

            // Resource Adjuster — one of the six essentials; reduces texture loss.
            Browser("resourceadjuster", "Resource Adjuster", "Community",
                new PlacementRule(PlacementKind.NamedFiles,
                    Files: ["GTAV.ResourceAdjuster.asi", "ResourceAdjuster.ini"]),
                order: 5, required: true, aliases: ["ResourceAdjuster"]),

            Browser("simpletrainer", "Simple Trainer", "sjaak327",
                new PlacementRule(PlacementKind.NamedFiles, Files: ["TrainerV.asi", "TrainerV.ini"]),
                order: 6, required: true, aliases: ["TrainerV"]),

            // ===== Callout Interface, before its copiers ==================
            Browser("calloutinterface", "Callout Interface", "Ryst",
                new PlacementRule(PlacementKind.SingleFile, Destination: "plugins", Files: ["CalloutInterface.dll"]),
                order: 10),

            // ===== Everything else ========================================
            // The plugins folder is dragged to the game root, landing at
            // game/plugins — so its contents place into the "plugins" destination.
            Browser("stoptheped", "Stop The Ped", "Nifty",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 40),

            // Grammar Police and LIAR both bundle RageNativeUI; the root copy
            // wins, so strip theirs before extracting. Grammar Police drags both
            // its lspdfr and plugins folders to the root, so its content root is
            // the folder holding both.
            Browser("grammarpolice", "Grammar Police", "TheDeadRiser",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "Grand Theft Auto V",
                    StripBeforeExtract: ["RageNativeUI.dll", "RAGENativeUI.dll"]), order: 40),

            // The Compulite plugin (game/plugins), separate from its citations data.
            Browser("compulite", "Compulite", "JoJo",
                new PlacementRule(PlacementKind.AutoDetect), order: 40),

            // Realistic Usable Charges and Citations → the Compulite data folder.
            Browser("charges", "Charges & Citations", "Compulite",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins/lspdfr/Compulite"), order: 41,
                aliases: ["Charge and Citations", "Charges and Citations", "Citations"]),

            Browser("heliassistance", "Heli Assistance", "PNWParksFan",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins/lspdfr"), order: 40,
                aliases: ["Heli Assist", "HeliAssist"]),

            // Ships Spotlight.dll loose beside a "Spotlight Resources" folder;
            // both belong under plugins, so take the whole root into plugins.
            Browser("spotlight", "Spotlight", "Yard1",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins"), order: 40),

            Browser("els", "ELS", "Lt.Caine",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "Installation Files/Grand Theft Auto V"),
                order: 40),

            Browser("betterelsreflections", "Better ELS Reflections", "Community",
                new PlacementRule(PlacementKind.SingleFile, Files: ["els.ini"],
                    SourceFolder: "Brighter with Higher Range and Brighter Takedowns"),
                dependsOn: ["els"], order: 41),

            Script("fastdraw", "Fast Draw"),
            Script("simplehud", "Simple HUD"),

            Browser("clearthewayv", "Clear The Way V", "Albo1125",
                new PlacementRule(PlacementKind.SingleFile, Destination: "plugins", Files: ["ClearTheWayV.dll"]), order: 40),

            Browser("radiorealism", "Radio Realism Alpha", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "lspdfr/audio/scanner", SourceFolder: "resident"),
                order: 40),

            Browser("immersiveeffects", "Immersive Effects", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 40),

            Browser("restrain", "Restrain", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 40),

            // LIAR (the LidarGun archive) drags IPCommon.dll and its plugins folder
            // from a GTA5 folder, so its content root is that folder.
            Browser("liar", "LIAR", "Opus49",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "Grand Theft Auto V",
                    StripBeforeExtract: ["RageNativeUI.dll", "RAGENativeUI.dll"]), order: 40,
                aliases: ["LidarGun", "Lidar"]),

            Browser("clipboard", "Clipboard", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 40),

            // ===== Last: they replace Stop The Ped files ==================
            Browser("searchitemsreborn", "Search Items Reborn", "Community",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 90),

            Browser("ultimatebackup", "Ultimate Backup", "Albo1125",
                new PlacementRule(PlacementKind.FolderContents, Destination: "plugins", SourceFolder: "plugins"), order: 90),

            // ===== Remaining guide mods (auto-placed) =====================
            Plugin("mdttextures", "MDT Textures", "Community", aliases: ["MDT Textures", "MDT"]),
            Plugin("radiorealismfr", "Radio Realism First Response", "Officer Porky",
                aliases: ["RadioRealismFR", "Radio Realism FR"]),
            Plugin("speedradarlite", "Speed Radar Lite", "JoJo", aliases: ["SpeedRadarLite", "Speed Radar"]),
            // Ships its .dll/.ini loose at the archive root, but it is an LSPDFR
            // plugin and must land under plugins, not the game root.
            Browser("richspoliceenhancement", "Rich's Police Enhancements", "Rich",
                new PlacementRule(PlacementKind.NamedFiles, Destination: "plugins",
                    Files: ["RichsPoliceEnhancements.dll", "RichsPoliceEnhancements.ini"]),
                order: 45, aliases: ["RichsPoliceEnhancements", "Rich Police", "Police Enhancement"]),
            Plugin("custompullover", "Custom Pullover", "Community", aliases: ["Custom Pullover"]),
            Plugin("deadlyweapons", "Deadly Weapons", "Community", aliases: ["Deadly Weapons"]),
            Plugin("riskiertrafficstops", "Riskier Traffic Stops", "ashopburgers",
                aliases: ["RiskierTrafficStops", "Riskier"]),
            Plugin("keepthedooropen", "Keep The Door Open", "Coro",
                aliases: ["KTFDO", "KeepTheDoorOpen", "Keep The Door"]),
            Plugin("stickywheels", "Sticky Wheels", "Coro", aliases: ["StickyWheels", "Sticky Wheels"]),

            // ===== Realism extras (auto-placed) ===========================
            Plugin("686callouts", "686 Callouts", "Community", aliases: ["686 Callouts", "686"]),
            Plugin("jobcenterv", "Job Center V", "Community", aliases: ["JobCenterV", "Job Center"]),
            Plugin("aet", "Ambient Events Tool", "Community", aliases: ["AET"]),
            Plugin("calloutlauncher", "Callout Launcher", "Community", aliases: ["Callout Launcher"]),
            Plugin("calloutresponse", "Callout Response", "Community", aliases: ["CalloutResponse", "Callout Response"]),
            Plugin("chilllscallouts", "ChillLS Callouts", "Community", aliases: ["ChillLSCallouts", "ChillLS"]),
            Plugin("kucheracallouts", "Kuchera Callouts", "Kuchera", aliases: ["Kuchera Callouts", "Kuchera"]),
            Plugin("mccallouts", "MC Callouts", "Community", aliases: ["MCCallouts", "MC Callouts"]),
            // Wraps everything in a "! GTAV MAIN DIRECTORY" folder that AutoDetect
            // cannot strip (it sits beside a second folder); take its contents to
            // the game root, where its plugins/ and lspdfr/ subtrees belong.
            Browser("policingredefined", "Policing Redefined", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "! GTAV MAIN DIRECTORY"),
                order: 45, aliases: ["PolicingRedefined", "Policing Redefined"]),
            Plugin("r57", "R57", "Community", aliases: ["R57_Main", "R57"]),
            Plugin("realtrafficai", "Real Traffic AI", "Community", aliases: ["Real Traffic AI", "Traffic AI"]),
            Plugin("resistplus", "Resist Plus", "Community", aliases: ["ResistPlus", "Resist Plus"]),
            // A siren-audio replacement: .awc/.rpf audio that has to be imported
            // through OpenIV, not copied into the game folder. Set aside for import.
            Manual("modernsirenpack", "Modern Siren Pack", "Community",
                aliases: ["MODERN SIREN PACK", "Modern Siren", "Siren Pack"]),
            Plugin("newsheli", "News Heli", "Community", aliases: ["NewsHeli", "News Heli"]),
            Plugin("simpledeath", "Simple Death", "Community", aliases: ["SimpleDeath", "Simple Death"]),
            Plugin("superevents", "Super Events", "Community", aliases: ["SuperEvents", "Super Events"]),
            Plugin("suspectsense", "Suspect Sense", "Community", aliases: ["SuspectSense", "Suspect Sense"]),
            Plugin("tacsimscallouts", "TACSIMS Casual Callouts", "TACSIMS",
                aliases: ["TACSIMS Casual Callouts", "TACSIMS"]),
            Plugin("trafficyield", "Traffic Yield", "Community", aliases: ["Traffic Yield"]),
            Plugin("vhud", "vHUD", "Community", aliases: ["Vhud", "vHUD"]),
            Plugin("fulltraffic", "Full Traffic", "Community", aliases: ["FullTraffic", "Full Traffic"]),
            // Just a frontend.xml — a pause-menu replacement that has to be imported
            // into update.rpf through OpenIV; a loose copy at the game root does
            // nothing, so set it aside for manual import.
            Manual("frontend", "Frontend", "Community", aliases: ["frontend"]),
            Plugin("homeownership", "Home Ownership", "Community", aliases: ["HomeOwnership", "Home Ownership"]),
            Plugin("openworldinteriors", "Open World Interiors", "Community",
                aliases: ["OpenWorldInteriors", "Open World"]),
            Plugin("holstersounds", "Holster Sounds", "Community", aliases: ["Holster Sounds", "Holster"]),

            // ===== Newer additions with non-standard layouts ==============
            // XtremeTool ships its .asi/.ini under a "Put in main folder" folder,
            // which is exactly where the game root is — take that folder's contents.
            Browser("xtremetool", "XtremeTool", "Community",
                new PlacementRule(PlacementKind.FolderContents, SourceFolder: "Put in main folder"),
                order: 45, aliases: ["XtremeTool", "Xtreme Tool"]),

            // Simmy Car Lots is a loose SHVDN script (.dll + .ini) that loads from
            // the scripts folder, not the game root.
            Browser("simmycarlots", "Simmy Car Lots", "Simmy",
                new PlacementRule(PlacementKind.NamedFiles, Destination: "scripts",
                    Files: ["SimmyCarLots.dll", "SimmyCarLotConfig.ini"]),
                order: 45, aliases: ["SimmyCarLots", "Simmy Car Lots"]),

            // SPGR ships a scripts/ tree (plus a .git folder that IsJunk drops);
            // taking only the scripts subtree lands it cleanly and skips the rest.
            Browser("spgr", "SPGR Mod Files", "SPGR",
                new PlacementRule(PlacementKind.FolderContents, Destination: "scripts", SourceFolder: "scripts"),
                order: 45, aliases: ["SPGR Mod FIles", "SPGR Mod Files", "SPGR"]),

            // ===== World & map mods (OpenIV import only) ==================
            // These are .oiv packages, add-on DLCs (dlc.rpf + a dlclist edit) or
            // loose .ymap maps — none of which a copy-installer can apply. Their
            // whole contents go to the OpenIV import folder for the user.
            Manual("worldofvariety", "World of Variety", "Aimless",
                aliases: ["world of variety", "World of Variety", "WoV"]),
            Manual("dnxchiliadtown", "DNX Chiliad Town", "DANIX",
                aliases: ["DNX Chiliad Town", "Chiliad Town"]),
            Manual("dnxsenoraroad", "DNX New Grand Senora Desert Road", "DANIX",
                aliases: ["DNX New Grand Senora Desert Road", "Grand Senora Desert Road", "Senora Desert Road"]),
            Manual("dnxcalafiaroads", "DNX New Calafia Roads", "DANIX",
                aliases: ["DNX New Calafia Roads", "Calafia Roads", "New Calafia"]),
            Manual("dnxpaletohighway", "DNX Better Paleto Bay Highway", "DANIX",
                aliases: ["DNX Better Paleto Bay Highway", "Paleto Bay Highway"]),
            Manual("dnxchiliadstateroads", "DNX Better Chiliad State Roads", "DANIX",
                aliases: ["DNX Better Chiliad State roads", "Chiliad State roads", "Chiliad State"]),
            Manual("dnxbraddockpass", "DNX Better Braddock Pass Highway", "DANIX",
                aliases: ["DNX Better Braddock Pass Highway", "Braddock Pass"]),

            // Visual/gore overhauls shipped as OpenIV .oiv packages — run through
            // OpenIV's Package Installer, so set them aside for manual import.
            Manual("gtavremastered", "GTA V Remastered Enhanced", "Community",
                aliases: ["GTA V Remastered Enhanced", "GTA V Remastered", "Remastered Enhanced"]),
            Manual("ero", "E.R.O", "MiGGousT",
                aliases: ["E.R.O 1.9.4", "MiGGousT", "E.R.O by MiGGousT"]),
            Manual("improvementsingore", "Improvements In Gore", "Community",
                aliases: ["Improvements In Gore", "ImprovementsInGore", "Improvements in Gore"]),
        };

        return mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    private static ModDefinition Github(
        string id, string name, string author, PlacementRule placement,
        string? repo = null, CompatibilityAnchor anchor = CompatibilityAnchor.LspdfrApi, int order = 100,
        bool required = false, IReadOnlyList<string>? aliases = null) =>
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
            Required = required,
            Aliases = aliases ?? [],
        };

    private static ModDefinition Browser(
        string id, string name, string author, PlacementRule placement,
        IReadOnlyList<string>? dependsOn = null, int order = 100, IReadOnlyList<string>? aliases = null,
        bool required = false) =>
        new()
        {
            Id = id,
            Name = name,
            Author = author,
            Source = SourceKind.Browser,
            Placement = placement,
            DependsOn = dependsOn ?? [],
            Order = order,
            Aliases = aliases ?? [],
            Required = required,
        };

    /// <summary>
    /// A plugin that follows the standard LSPDFR layout, placed by AutoDetect —
    /// wrapper folders stripped, recognised game folders merged. Used for the long
    /// tail of mods whose archive is just "extract into the game folder".
    /// </summary>
    private static ModDefinition Plugin(string id, string name, string author, IReadOnlyList<string>? aliases = null) =>
        new()
        {
            Id = id,
            Name = name,
            Author = author,
            Source = SourceKind.Browser,
            Placement = new PlacementRule(PlacementKind.AutoDetect),
            Order = 45,
            Aliases = aliases ?? [],
        };

    /// <summary>
    /// A mod that cannot be auto-installed — its whole archive is set aside to the
    /// OpenIV import folder for the user to apply through OpenIV. Ordered after the
    /// game-folder mods, since it writes nothing there.
    /// </summary>
    private static ModDefinition Manual(string id, string name, string author, IReadOnlyList<string>? aliases = null) =>
        new()
        {
            Id = id,
            Name = name,
            Author = author,
            Source = SourceKind.Browser,
            Placement = new PlacementRule(PlacementKind.ManualImport),
            Order = 50,
            Aliases = aliases ?? [],
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
