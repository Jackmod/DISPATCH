namespace Dispatch.Core.Configuration;

/// <summary>The config edits for one mod: where its file is and what to set in it.</summary>
/// <param name="ModId">The mod these edits belong to.</param>
/// <param name="FileHints">
/// Candidate paths to the config file, relative to the game folder, tried in
/// order; the filename may be a glob. The first that exists is edited.
/// </param>
/// <param name="Settings">The values to write, named as the guide names them.</param>
public sealed record ModConfig(
    string ModId,
    IReadOnlyList<string> FileHints,
    IReadOnlyList<ConfigSetting> Settings);

/// <summary>
/// Every config change the install guide specifies, as data.
/// </summary>
/// <remarks>
/// This is the guide's Phases 4, 5, 9, 13 and 17 turned into rows. The values are
/// exactly those the guide gives — chosen so that no two actions collide on a
/// key, which <c>KeybindClashDetector</c> checks holds. Officer-specific values
/// are placeholders filled in per install. Nothing here is applied blindly: the
/// writer only changes keys that actually exist in the file it finds.
/// </remarks>
public static class ConfigCatalogue
{
    private static ConfigSetting S(string name, string value, ConfigMatch match = ConfigMatch.Exact) =>
        new(name, value, match);

    /// <summary>Every mod's config edits, in install order.</summary>
    public static readonly IReadOnlyList<ModConfig> All =
    [
        // ===== Phase 4 — LSPDFR key configuration =========================
        new ModConfig("lspdfr",
            ["Plugins/LSPDFR/KeyBindings.ini", "Plugins/LSPDFR/Keys.ini", "Plugins/LSPDFR/*Key*.ini", "LSPDFR.ini"],
            [
                S("Pursuit Menu Controller Key", "None"),
                S("Crime Report Controller Key", "None"),
                S("Stop Peds Key", "I"),
                S("Perform Arrest Key", "I"),
                S("Chase Abort Join Key", "None"),
                S("Chase Abort Join Controller Key", "None"),
                S("Traffic Stop Start Controller Key", "None"),
                S("Traffic Stop Interact Key", "I"),
                S("Traffic Stop Interact Controller Key", "None"),
                S("Toggle Police Computer Controller Key", "None"),
                S("Backup Menu Key", "None"),
                S("Backup Menu Controller Key", "None"),

                // ===== Phase 5 — LSPDFR settings (same file family) =======
                S("Main Preload Models", "false"),
                S("Ambient Disable Escape Suspect Counter", "true"),
                S("Chase Disable Camera Focus", "true"),
                S("Ambient Disabled Player Flashlight Override", "true"),
            ]),

        // ===== Phase 9 — Simple Trainer ===================================
        new ModConfig("simpletrainer",
            ["TrainerV.ini"],
            [
                S("Spawn A Driver Key", "0"),
                S("Add Waypoint Key", "0"),
            ]),

        // ===== Phase 13 — batch 1 =========================================
        new ModConfig("skincontrol",
            ["SkinControl.ini"],
            [S("Hotkey", "9")]),

        new ModConfig("rphdeletevehicle",
            ["Plugins/RPHDeleteVehicle.ini", "Plugins/*Delete*Vehicle*.ini"],
            [
                S("Delete Key", "D"),
                // The guide keeps the Shift modifier, so the binding is Shift+D.
                S("Delete Modifier Key", "Left Shift"),
            ]),

        new ModConfig("clipboard",
            ["Plugins/LSPDFR/Clipboard.ini", "Plugins/LSPDFR/*Clipboard*.ini"],
            [
                S("Clipboard Key", "T"),
                S("Notepad Key", "Y"),
                S("Clipboard Modifier Key", "Left Control"),
            ]),

        new ModConfig("compulite",
            ["Plugins/LSPDFR/Compulite/Compulite.ini", "Plugins/LSPDFR/*Compulite*.ini"],
            [
                S("Open Computer Key", "X"),
                S("Give Citation Key", "X"),
                S("Give Citation Modifier Key", "Left Shift"),
                S("Open Computer Controller Button", "None"),
                S("Court case waiting duration", "24"),
                S("Pause game when opening", "No"),
            ]),

        new ModConfig("liar",
            ["Plugins/LSPDFR/LIAR.ini", "Plugins/LSPDFR/*LIAR*.ini"],
            [
                S("LIAR Key", "1"),
                S("Position X", "612"),
                S("Position Y", "755"),
                S("Scale", "71"),
                S("Volume", "5"),
                S("HUD Colour", "1"),
            ]),

        new ModConfig("calloutinterface",
            ["Plugins/LSPDFR/CalloutInterface.ini", "Plugins/LSPDFR/*CalloutInterface*.ini"],
            [
                S("Callout Menu Key", "F10"),
                S("Toggle Terminal Key", "NumPad7"),
                S("Toggle ALPR Key", "D"),
                S("Hold Interval", "300"),
                S("MDT Call Sign", "{callsign}"),
                S("MDT Position X", "1353"),
                S("MDT Position Y", "698"),
                S("MDT Scale", "91"),
                S("MDT Timeout", "15"),
                S("MDT Sound Display", "4"),
                S("Postal Code Enabled", "True"),
                S("Postal Code Set", "virus_City"),
                S("Postal Code Position X", "318"),
                S("Postal Code Position Y", "17"),
                S("Postal Code Scale", "47"),
                S("Plate Enabled", "True"),
                S("Plate Position X", "495"),
                S("Plate Position Y", "907"),
                S("Plate Scale", "60"),
                S("Auto Tab", "True", ConfigMatch.Contains),
                S("Auto Blip", "True"),
                S("Blip Enabled", "True"),
            ]),

        new ModConfig("speedradarlite",
            ["Plugins/LSPDFR/SpeedRadar.ini", "Plugins/LSPDFR/*Speed*Radar*.ini", "Plugins/LSPDFR/*Radar*.ini"],
            [
                S("Increase Threshold Key", "I"),
                S("Decrease Threshold Key", "O"),
                S("Threshold Modifier Key", "Left Shift"),
                S("Initial Speed Threshold", "55"),
            ]),

        new ModConfig("fastdraw",
            ["scripts/FastDraw.ini", "scripts/*Fast*Draw*.ini"],
            [S("Menu Key", "NONE")]),

        // ===== Phase 17 — batch 2 =========================================
        new ModConfig("ingamescreenshot",
            ["scripts/InGameScreenshot.ini", "scripts/*Screenshot*.ini"],
            [S("Screenshot Key", "K")]),

        new ModConfig("simplehud",
            ["scripts/SimpleHUD.ini", "scripts/*Simple*HUD*.ini"],
            [
                S("Direction Position X", "292"),
                S("Direction Scale", "51"),
                S("Road Position X", "312"),
                S("Postal Enabled", "false"),
                S("Time Position Y", "912"),
                S("Time Format", "12"),
                S("Time Enabled", "true"),
                S("Toggle Key", "B"),
                S("Modifier Key", "NONE"),
                S("Menu Enabled", "true"),
            ]),

        new ModConfig("spotlight",
            ["Plugins/spotlight_resources/General.ini", "Plugins/spotlight_resources/*General*.ini",
             "Plugins/spotlight_resources/*.ini"],
            [
                S("Editor Key", "F6"),
                S("Keyboard toggle key", "S"),
                S("Controller modifier key", "NONE"),
                S("Mouse toggle key", "S"),
            ]),

        new ModConfig("immersiveeffects",
            ["Plugins/ImmersiveEffects.ini", "Plugins/*Immersive*.ini"],
            [S("Menu Key", "F2")]),

        new ModConfig("dashcamv",
            ["Plugins/DashCamV.ini", "Plugins/*DashCam*.ini"],
            [
                S("Measurement system", "1"),
                S("Unit Name", "{officer}"),
                S("State", "San Andreas"),
                S("Remote Toggle Key", "I"),
                S("Remote View Gamepad Toggle", "NONE", ConfigMatch.Contains),
                S("Department", "{department}", ConfigMatch.Contains),
                S("Black and white filter", "false"),
            ]),

        new ModConfig("restrain",
            ["Plugins/Restrain.ini", "Plugins/*Restrain*.ini"],
            [S("Restrain Key", "E")]),

        new ModConfig("grammarpolice",
            ["Plugins/LSPDFR/GrammarPolice/custom/*.ini", "Plugins/LSPDFR/GrammarPolice/default/*.ini",
             "Plugins/LSPDFR/GrammarPolice/*.ini"],
            [
                S("Call Sign", "{callsign}"),
                S("Agency", "\"IMMERSIVE\""),
                S("Dispatch Key", "0"),
                S("Interface Key", "F4"),
                S("Settings Key", "F7"),
                S("Radio Key", "O"),
                S("Radio Modifier Key", "Left Control"),
                S("Show Notifications", "True"),
                S("Player Status", "True"),
                S("Show Target Plate", "True"),
                S("Status Text Position X", "489"),
                S("Status Text Position Y", "980"),
                S("Status Text Scale", "47"),
                S("Radial Position X", "625"),
                S("Radio Position Y", "669"),
                S("Radio Scale", "43"),
                S("PTT Hold To Talk", "True"),
                S("Preface Response", "2"),
                S("Enable Traffic Stop", "True"),
                S("Attempt To Initiate Pursuit", "True"),
                S("Use Generic Response", "True"),
                S("Officer Backup Air", "True"),
                S("Use Natives", "False", ConfigMatch.Contains),
            ]),

        new ModConfig("baitcar",
            ["Plugins/LSPDFR/BaitCar.ini", "Plugins/LSPDFR/*BaitCar*.ini", "Plugins/*BaitCar*.ini"],
            [S("Main Menu Key", "F11")]),

        new ModConfig("heliassistance",
            ["Plugins/LSPDFR/HeliAssistance.ini", "Plugins/LSPDFR/*Heli*.ini"],
            [
                S("Player Name", "{officer}"),
                S("Unit Name", "{airunit}"),
            ]),

        new ModConfig("radiorealismfr",
            ["Plugins/LSPDFR/OfficerPorky.ini", "Plugins/LSPDFR/*Porky*.ini", "Plugins/LSPDFR/*RadioRealism*.ini"],
            [
                S("Display Street Detection Notification", "True"),
                S("Enable Auto Ped ID Check", "True"),
                S("Key To Play Backup Animation", "NONE"),
            ]),

        new ModConfig("riskiertrafficstops",
            ["Plugins/LSPDFR/RiskierTrafficStops.ini", "Plugins/LSPDFR/*Riskier*.ini"],
            [S("Chance", "30", ConfigMatch.Contains)]),

        new ModConfig("stoptheped",
            ["Plugins/LSPDFR/StopThePed.ini", "Plugins/LSPDFR/StopThePedConfig.ini", "Plugins/LSPDFR/*StopThePed*.ini"],
            [
                S("Shortcut key to pat down", "F9"),
                S("Key to call transport", "D9"),
                S("Button to tackle", "X"),
                S("Button to boost player speed", "A"),
                S("Take control of all peds arrested by LSPDFR", "NO"),
                S("Force search result full screen", "NO"),
                S("Glowing stick", "NO", ConfigMatch.Contains),
                S("Realistic weapon system", "NO"),
                S("Prisoner transport backup enabled", "YES"),
                S("Use nearest cop as prisoner transport", "YES"),
            ]),

        new ModConfig("ultimatebackup",
            ["Plugins/LSPDFR/UltimateBackup.ini", "Plugins/LSPDFR/*UltimateBackup*.ini", "Plugins/*UltimateBackup*.ini"],
            [
                S("Toggle Menu Key", "U"),
                S("Perimeters code 2 siren lights on", "NO"),
            ]),
    ];

    /// <summary>The config edits for one mod, or null when none are defined.</summary>
    public static ModConfig? For(string modId) =>
        All.FirstOrDefault(c => string.Equals(c.ModId, modId, StringComparison.Ordinal));
}
