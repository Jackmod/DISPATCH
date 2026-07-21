namespace Dispatch.Core.Configuration;

/// <summary>The config edits for one mod: where its file is and what to set in it.</summary>
/// <param name="ModId">The mod these edits belong to.</param>
/// <param name="FileHints">
/// Candidate paths to the config file, relative to the game folder, tried in
/// order; the filename may be a glob. The first that exists is edited.
/// </param>
/// <param name="Settings">The values to write, named by the file's real key.</param>
public sealed record ModConfig(
    string ModId,
    IReadOnlyList<string> FileHints,
    IReadOnlyList<ConfigSetting> Settings);

/// <summary>
/// Every config change the install guide specifies, as data.
/// </summary>
/// <remarks>
/// This is the guide's Phases 4, 5, 9, 13 and 17 turned into rows — but keyed to
/// each mod's <em>real</em> ini key names, file paths and value formats, read from
/// the actual mod archives rather than the guide's friendly labels. That distinction
/// is the whole game: the writer only changes a key that already exists in the file
/// (<see cref="IniConfigWriter"/>), so a name that does not match the file's real key
/// is silently written nowhere. Each setting's <c>Name</c> is therefore the file's
/// exact key, which normalises to itself and matches cleanly.
///
/// <para>
/// A few mods need more than one file (LSPDFR keeps its keys in <c>keys.ini</c> and
/// its settings in <c>lspdfr.ini</c>) — those are two rows with the same mod id, both
/// of which the installer applies. Values are in each file's own dialect: WinForms
/// key names (<c>LShiftKey</c>, <c>NumPad7</c>, <c>D0</c>), the mod's own <c>yes/no</c>
/// or <c>True/False</c> booleans, and pixel or fractional coordinates as that version
/// of the mod expects. Where a mod's current release changed a value's units from what
/// the guide assumed (Simple HUD moved to fractional positions), the stale values are
/// left out rather than written wrong.
/// </para>
/// </remarks>
public static class ConfigCatalogue
{
    private static ConfigSetting S(string name, string value, ConfigMatch match = ConfigMatch.Exact, string? section = null) =>
        new(name, value, match, section);

    /// <summary>Every mod's config edits, in install order.</summary>
    public static readonly IReadOnlyList<ModConfig> All =
    [
        // ===== Phase 4 — LSPDFR key configuration (lspdfr/keys.ini) ========
        // Flat file at the game root's lspdfr folder, no sections. Keys are
        // SCREAMING_SNAKE with a _Key / _ControllerKey suffix.
        new ModConfig("lspdfr",
            ["lspdfr/keys.ini", "lspdfr/Keys.ini"],
            [
                S("STOP_PEDS_Key", "I"),
                S("PERFORM_ARREST_Key", "I"),
                S("TRAFFICSTOP_INTERACT_Key", "I"),
                S("CHASE_ABORT_JOIN_Key", "None"),
                S("BACKUP_MENU_Key", "None"),
                S("PURSUIT_MENU_ControllerKey", "None"),
                S("CRIME_REPORT_ControllerKey", "None"),
                S("CHASE_ABORT_JOIN_ControllerKey", "None"),
                S("TRAFFICSTOP_START_ControllerKey", "None"),
                S("TRAFFICSTOP_INTERACT_ControllerKey", "None"),
                S("TOGGLE_POLICECOMPUTER_ControllerKey", "None"),
                S("BACKUP_MENU_ControllerKey", "None"),
            ]),

        // ===== Phase 5 — LSPDFR settings (lspdfr/lspdfr.ini) ==============
        // Dotted keys (Section.Setting), lower-case true/false.
        new ModConfig("lspdfr",
            ["lspdfr/lspdfr.ini", "lspdfr/LSPDFR.ini"],
            [
                S("Main.PreloadAllModels", "false"),
                S("Ambient.DisableEscapedSuspectEncounter", "true"),
                S("Chase.DisableCameraFocus", "true"),
                S("Ambient.DisablePlayerFlashlightOverride", "true"),
            ]),

        // ===== Phase 9 — Simple Trainer (TrainerV.ini) ====================
        // Numeric virtual-key codes; 0 disables the bind.
        new ModConfig("simpletrainer",
            ["TrainerV.ini", "trainerv.ini"],
            [
                S("SpawnADriverKey", "0"),
                S("AddWayPoint", "0"),
            ]),

        // ===== Phase 13 — batch 1 =========================================
        new ModConfig("clipboard",
            ["plugins/LSPDFR/Clipboard.ini"],
            [
                S("ClipboardKey", "T"),
                S("NotepadKey", "Y"),
                S("ClipboardModifierKey", "LControlKey"),
            ]),

        new ModConfig("compulite",
            ["plugins/LSPDFR/CompuLite.ini", "plugins/LSPDFR/*Compulite*.ini"],
            [
                S("OpenComputerKey", "X"),
                S("GiveCitationKey", "X"),
                S("GiveCitationModifierKey", "LShiftKey"),
                S("OpenComputerButton", "None"),
                S("CourtCaseWaitingTime", "24"),
                S("IsPausedWhenOpen", "no"),
            ]),

        new ModConfig("liar",
            ["plugins/LSPDFR/LidarGun.ini", "plugins/LSPDFR/*Lidar*.ini", "plugins/LSPDFR/*LIAR*.ini"],
            [
                S("LidarKey", "NumPad1"),
                S("PosX", "612"),
                S("PosY", "755"),
                S("Scale", "71"),
                S("Volume", "5"),
                S("HudColor", "1"),
            ]),

        new ModConfig("calloutinterface",
            ["plugins/LSPDFR/CalloutInterface.ini", "plugins/LSPDFR/*CalloutInterface*.ini"],
            [
                S("CalloutMenuKey", "F10"),
                S("ToggleTerminalKey", "NumPad7"),
                S("ToggleALPRKey", "D"),
                S("HoldInterval", "300"),
                S("MDTCallsign", "{callsign}"),
                S("MDTPosX", "1353"),
                S("MDTPosY", "698"),
                S("MDTScale", "91"),
                S("MDTTimeout", "15"),
                S("MDTSoundOnDisplay", "4"),
                S("MDTSoundOnALPRHit", "5"),
                S("PostalCodeEnabled", "True"),
                S("PostalCodeSet", "virus_City"),
                S("PostalCodePosX", "318"),
                S("PostalCodePosY", "17"),
                S("PostalCodeScale", "47"),
                S("PlateEnabled", "True"),
                S("PlatePosX", "495"),
                S("PlatePosY", "907"),
                S("PlateScale", "60"),
                S("AutoBlip", "True"),
                S("BlipEnabled", "True"),
                // The [AutoTab] block: every entry on so a callout auto-fills its tabs.
                S("ALPR", "True", ConfigMatch.Exact, "AutoTab"),
                S("Incident", "True", ConfigMatch.Exact, "AutoTab"),
                S("Peds", "True", ConfigMatch.Exact, "AutoTab"),
                S("Vehicles", "True", ConfigMatch.Exact, "AutoTab"),
            ]),

        new ModConfig("speedradarlite",
            ["plugins/LSPDFR/SpeedRadarLite.ini", "plugins/LSPDFR/*Speed*Radar*.ini"],
            [
                // Note the mod's own misspelling: "Threashold" on the increase key.
                S("IncreaseThreashold", "I"),
                S("DecreaseThreasholdKey", "O"),
                S("ThresholdModifierKey", "LShiftKey"),
                S("SpeedThreshold", "55"),
            ]),

        new ModConfig("fastdraw",
            ["scripts/Fast_Draw_Settings.ini", "scripts/*Fast*Draw*.ini"],
            [S("MENUKEY", "None")]),

        // ===== Phase 17 — batch 2 =========================================
        // Simple HUD moved to fractional 0..1 coordinates, so the guide's pixel
        // positions/scales no longer apply and are left out; only the still-valid
        // toggles are written.
        new ModConfig("simplehud",
            ["SimpleHUD.ini", "scripts/SimpleHUD.ini"],
            [
                S("PostalEnabled", "false"),
                S("TimeFormat", "12h"),
                S("TimeEnabled", "true"),
                S("ToggleKey", "B"),
                S("MenuEnabled", "true"),
            ]),

        new ModConfig("spotlight",
            ["plugins/spotlight_resources/General.ini", "plugins/spotlight_resources/*General*.ini"],
            [
                S("EditorKey", "F6"),
                // Toggle and Modifier repeat across [Keyboard]/[Controller]/[Mouse];
                // scope each so it lands in the right one.
                S("Toggle", "S", ConfigMatch.Exact, "Keyboard"),
                S("Toggle", "S", ConfigMatch.Exact, "Mouse"),
                S("Modifier", "None", ConfigMatch.Exact, "Controller"),
            ]),

        new ModConfig("immersiveeffects",
            ["plugins/Immersive Effects.ini", "plugins/*Immersive*.ini"],
            [
                S("MenuKey", "F2"),
                S("MenuKeyModifier", "LShiftKey"),
            ]),

        new ModConfig("restrain",
            ["plugins/Restrain The Deceased.ini", "plugins/*Restrain*.ini"],
            [S("RestrainKey", "E")]),

        new ModConfig("grammarpolice",
            ["plugins/LSPDFR/GrammarPolice/custom.ini", "plugins/LSPDFR/GrammarPolice/default.ini",
             "plugins/LSPDFR/GrammarPolice/*.ini"],
            [
                S("Callsign", "\"{callsign}\""),
                S("AgencyCodes", "\"IMMERSIVE\""),
                S("DispatchKey", "D0"),
                S("InterfaceKey", "F8"),
                S("SettingsKey", "F7"),
                S("SettingsModifier", "LControlKey"),
                S("RadioKey", "O"),
                S("RadioModifier", "LControlKey"),
                S("ShowNotifications", "true"),
                S("ShowPlayerStatus", "true"),
                S("ShowTargetPlate", "true"),
                S("StatusTextPosX", "489"),
                S("StatusTextPosY", "980"),
                S("RadioPosX", "625"),
                S("RadioPosY", "669"),
                S("RadioScale", "43"),
                S("HoldToTalk", "true"),
                S("PrefaceResponse", "2"),
                S("EnableTrafficStop", "true"),
                S("AttemptToInitiatePursuit", "true"),
                S("UseGenericResponse", "true"),
                S("OfferBackupAir", "true"),
            ]),

        new ModConfig("heliassistance",
            ["plugins/lspdfr/HeliAssistance.ini", "plugins/LSPDFR/HeliAssistance.ini", "plugins/lspdfr/*Heli*.ini"],
            [
                S("PlayerName", "{officer}"),
                S("UnitName", "{airunit}"),
            ]),

        new ModConfig("radiorealismfr",
            ["plugins/LSPDFR/RadioRealismFR.ini", "plugins/LSPDFR/*Porky*.ini", "plugins/LSPDFR/*RadioRealism*.ini"],
            [
                S("DisplayStreetDetectionNotification", "true"),
                S("EnableAutoPedIDCheck", "true"),
                // Key name literally has spaces in this file; normalises the same.
                S("Backup Menu Key", "None"),
            ]),

        new ModConfig("riskiertrafficstops",
            ["plugins/LSPDFR/RiskierTrafficStops.ini", "plugins/LSPDFR/*Riskier*.ini"],
            [S("Chance", "30")]),

        new ModConfig("stoptheped",
            ["plugins/LSPDFR/StopThePed.ini", "plugins/LSPDFR/*StopThePed*.ini"],
            [
                S("SearchKey", "F9"),
                S("CallTransportKey", "D9"),
                // Keyboard tackle is left at its default: the guide puts tackle on X
                // for the controller only, and X on the keyboard is Compulite's
                // hold-to-open. The controller button is set in the control catalogue.
                S("SprintBoostKey", "A"),
                S("TakeOverAllArrests", "no"),
                S("ForceSearchResultFullScreen", "no"),
                S("OnFootTrafficUseWand", "no"),
                // "Turn the realistic weapon system on at startup" — a generic key
                // name, so scope it to its section.
                S("EnabledOnStartup", "no", ConfigMatch.Exact, "RealisticWeaponSystem"),
                S("PrisonerTransportEnabled", "yes"),
                S("RecruitNearestCopForTransport", "yes"),
                // Every prisoner-transport siren/light entry off.
                S("PrisonerTransportSiren", "no", ConfigMatch.Contains),
            ]),

        new ModConfig("ultimatebackup",
            ["plugins/LSPDFR/UltimateBackup.ini", "plugins/LSPDFR/*UltimateBackup*.ini"],
            [
                S("ToggleMenuKey", "U"),
                S("IsCode2SirenLightsOn", "no"),
            ]),
    ];

    /// <summary>The config edits for one mod, or null when none are defined.</summary>
    public static ModConfig? For(string modId) =>
        All.FirstOrDefault(c => string.Equals(c.ModId, modId, StringComparison.Ordinal));
}
