namespace Dispatch.Core.Configuration;

/// <summary>
/// Every mod setting Dispatch can edit, as a declarative table: the guide's
/// thirty-config-file setup expressed as data.
/// </summary>
/// <remarks>
/// The controls catalogue owns the keybinds; this owns everything else — the
/// positions, scales, thresholds and on/off flags that the manual install has
/// people editing by hand across thirty files, any single typo breaking things
/// silently. Each row says where the value lives and what shape it is, so the
/// screen can render a switch or a slider and the writer can put the mod's own
/// literal back. The values are the ones the guide settles on; a person can
/// change any of them, but out of the box they match a setup known to work.
/// </remarks>
public static class SettingsCatalogue
{
    /// <summary>Every editable setting, in display order.</summary>
    public static readonly IReadOnlyList<ModSetting> Settings =
    [
        // ===== LSPDFR core =============================================
        Toggle("lspdfr.preloadModels", "Preload models", "lspdfr/LSPDFR Configuration Setting.ini", "PreloadModels",
            "Loads every model up front. Turning it off is the main fix for texture loss in a heavy setup.",
            on: "true", off: "false", defaultOn: false, plugin: "LSPDFR", category: "Performance"),
        Toggle("lspdfr.escapeCounter", "Hide escape-suspect counter", "lspdfr/LSPDFR Configuration Setting.ini", "DisableEscapeSuspectCounter",
            "Removes the on-screen counter that tracks a fleeing suspect.",
            on: "true", off: "false", defaultOn: true, plugin: "LSPDFR", category: "Interface"),
        Toggle("lspdfr.cameraFocus", "Disable chase camera focus", "lspdfr/LSPDFR Configuration Setting.ini", "DisableCameraFocus",
            "Stops the camera yanking toward a passing suspect in the middle of a chase.",
            on: "true", off: "false", defaultOn: true, plugin: "LSPDFR", category: "Camera"),
        Toggle("lspdfr.flashlight", "Hand flashlight to Stop The Ped", "lspdfr/LSPDFR Configuration Setting.ini", "DisablePlayerFlashlightOverride",
            "Lets Stop The Ped own the flashlight instead of LSPDFR's built-in one.",
            on: "true", off: "false", defaultOn: true, plugin: "LSPDFR", category: "Suspects"),

        // ===== Callout Interface =======================================
        Text("ci.callsign", "MDT callsign", "plugins/LSPDFR/CalloutInterface.ini", "MDTCallSign",
            "The callsign shown on the mobile data terminal. Kept in upper case to match dispatch.",
            plugin: "Callout Interface", category: "Identity", profile: ProfileField.CallsignUpper),
        Number("ci.holdInterval", "Hold interval", "plugins/LSPDFR/CalloutInterface.ini", "HoldInterval",
            "How long a hold-to-act key must be held, in milliseconds.",
            min: 100, max: 1000, step: 50, def: "300", unit: "ms", plugin: "Callout Interface", category: "Input"),
        Number("ci.mdtX", "MDT position X", "plugins/LSPDFR/CalloutInterface.ini", "MDTPositionX",
            "Horizontal position of the terminal on screen.",
            min: 0, max: 2000, step: 1, def: "1353", plugin: "Callout Interface", category: "Terminal"),
        Number("ci.mdtY", "MDT position Y", "plugins/LSPDFR/CalloutInterface.ini", "MDTPositionY",
            "Vertical position of the terminal on screen.",
            min: 0, max: 1200, step: 1, def: "698", plugin: "Callout Interface", category: "Terminal"),
        Number("ci.mdtScale", "MDT scale", "plugins/LSPDFR/CalloutInterface.ini", "MDTScale",
            "Size of the terminal.", min: 0, max: 100, step: 1, def: "91", plugin: "Callout Interface", category: "Terminal"),
        Number("ci.mdtTimeout", "MDT timeout", "plugins/LSPDFR/CalloutInterface.ini", "MDTTimeout",
            "How long the terminal stays up with no input, in seconds.",
            min: 0, max: 120, step: 1, def: "15", unit: "s", plugin: "Callout Interface", category: "Terminal"),
        Toggle("ci.postalEnabled", "Show postal code", "plugins/LSPDFR/CalloutInterface.ini", "PostalCodeEnabled",
            "Displays the nearest postal code near the minimap.",
            on: "True", off: "False", defaultOn: true, plugin: "Callout Interface", category: "Postal"),
        Number("ci.postalX", "Postal position X", "plugins/LSPDFR/CalloutInterface.ini", "PostalCodePositionX",
            "Horizontal position of the postal readout.", min: 0, max: 2000, step: 1, def: "318", plugin: "Callout Interface", category: "Postal"),
        Number("ci.postalY", "Postal position Y", "plugins/LSPDFR/CalloutInterface.ini", "PostalCodePositionY",
            "Vertical position of the postal readout.", min: 0, max: 1200, step: 1, def: "17", plugin: "Callout Interface", category: "Postal"),
        Number("ci.postalScale", "Postal scale", "plugins/LSPDFR/CalloutInterface.ini", "PostalCodeScale",
            "Size of the postal readout.", min: 0, max: 100, step: 1, def: "47", plugin: "Callout Interface", category: "Postal"),
        Toggle("ci.plateEnabled", "Show plate readout", "plugins/LSPDFR/CalloutInterface.ini", "PlateEnabled",
            "Displays the plate of a scanned vehicle.",
            on: "True", off: "False", defaultOn: true, plugin: "Callout Interface", category: "Plate"),
        Toggle("ci.autoBlip", "Auto blip", "plugins/LSPDFR/CalloutInterface.ini", "AutoBlip",
            "Automatically drops a map blip for a new call.",
            on: "True", off: "False", defaultOn: true, plugin: "Callout Interface", category: "Map"),

        // ===== Grammar Police ==========================================
        Text("gp.callsign", "Call sign", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "CallSign",
            "Must match your Callout Interface callsign, or dispatch will not recognise you.",
            plugin: "Grammar Police", category: "Identity", profile: ProfileField.Callsign),
        Text("gp.agency", "Agency phrase", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "Agency",
            "The agency dispatch says on the radio. Kept in quotes as the mod expects.",
            plugin: "Grammar Police", category: "Identity", quoted: true, def: "IMMERSIVE"),
        Choice("gp.preface", "Dispatch preface", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "PrefaceResponse",
            "How dispatch answers after your callsign.",
            [new("\"This is dispatch\"", "1"), new("\"Go ahead\" (recommended)", "2"), new("Repeats your callsign", "3")],
            def: "2", plugin: "Grammar Police", category: "Voice"),
        Toggle("gp.ptt", "Push to talk", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "PTTHoldToTalk",
            "Hold the radio key to speak, rather than toggling it.",
            on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Voice"),
        Toggle("gp.notifications", "Show notifications", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "ShowNotifications",
            "On-screen confirmation of what dispatch heard.",
            on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Interface"),
        Toggle("gp.trafficStop", "Enable traffic stops", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "EnableTrafficStop",
            "Lets you run a traffic stop by voice.",
            on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Voice"),
        Toggle("gp.airBackup", "Air backup by voice", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "OfficerBackupAir",
            "Lets you call an air unit by voice.",
            on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Voice"),

        // ===== Stop The Ped ============================================
        Toggle("stp.takeControl", "Take control of arrested peds", "plugins/StopThePed.ini", "TakeControlOfArrested",
            "Off means other officers transport their own arrests instead of leaving them beside you.",
            on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Suspects"),
        Toggle("stp.fullScreenSearch", "Full-screen search results", "plugins/StopThePed.ini", "ForceSearchFullScreen",
            "Off shows results above the minimap instead of freezing the game.",
            on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Suspects"),
        Toggle("stp.parkingWand", "Parking wand", "plugins/StopThePed.ini", "GlowingStick",
            "The glowing traffic-direction wand.",
            on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Equipment"),
        Toggle("stp.realisticWeapons", "Realistic weapon system", "plugins/StopThePed.ini", "RealisticWeapons",
            "Off by default; it is a toggle you enable in-game when you want it.",
            on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Suspects"),
        Toggle("stp.transportBackup", "Prisoner transport backup", "plugins/StopThePed.ini", "PrisonerTransportBackup",
            "Allows calling a transport unit for a prisoner.",
            on: "YES", off: "NO", defaultOn: true, plugin: "Stop The Ped", category: "Transport"),
        Toggle("stp.nearestCop", "Nearest cop transports", "plugins/StopThePed.ini", "UseNearestCopAsTransport",
            "A backup unit on scene takes the prisoner instead of you waiting for a van.",
            on: "YES", off: "NO", defaultOn: true, plugin: "Stop The Ped", category: "Transport"),

        // ===== Compulite (Charges & Citations) =========================
        Number("compulite.courtWait", "Court waiting duration", "plugins/lspdfr/Compulite/config.ini", "CourtWaitDuration",
            "In-game hours before a court case comes up.",
            min: 0, max: 72, step: 1, def: "24", unit: "h", plugin: "Charges & Citations", category: "Court"),
        Toggle("compulite.pause", "Pause game when opening", "plugins/lspdfr/Compulite/config.ini", "PauseOnOpen",
            "Off keeps the world moving while you write.",
            on: "Yes", off: "No", defaultOn: false, plugin: "Charges & Citations", category: "General"),

        // ===== Simple HUD ==============================================
        Number("hud.directionX", "Direction position X", "scripts/SimpleHUD.ini", "DirectionPositionX",
            "Horizontal position of the compass direction.", min: 0, max: 2000, step: 1, def: "292", plugin: "Simple HUD", category: "Layout"),
        Number("hud.directionScale", "Direction scale", "scripts/SimpleHUD.ini", "DirectionScale",
            "Size of the compass direction.", min: 0, max: 100, step: 1, def: "51", plugin: "Simple HUD", category: "Layout"),
        Number("hud.roadX", "Road position X", "scripts/SimpleHUD.ini", "RoadPositionX",
            "Horizontal position of the street name.", min: 0, max: 2000, step: 1, def: "312", plugin: "Simple HUD", category: "Layout"),
        Toggle("hud.postal", "Show postal on HUD", "scripts/SimpleHUD.ini", "PostalEnabled",
            "Off by default because Callout Interface's postal is better positioned. Do not run both.",
            on: "true", off: "false", defaultOn: false, plugin: "Simple HUD", category: "Layout"),
        Choice("hud.timeFormat", "Time format", "scripts/SimpleHUD.ini", "TimeFormat",
            "Clock format on the HUD.",
            [new("12-hour", "12"), new("24-hour", "24")], def: "12", plugin: "Simple HUD", category: "Clock"),
        Toggle("hud.menu", "HUD menu enabled", "scripts/SimpleHUD.ini", "MenuEnabled",
            "Must be on or the HUD menu will not open at all.",
            on: "true", off: "false", defaultOn: true, plugin: "Simple HUD", category: "General"),

        // ===== Dash Cam V ==============================================
        Choice("dashcam.units", "Measurement system", "plugins/DashCamV.ini", "MeasurementSystem",
            "Units shown on the dash cam overlay.",
            [new("Metric (km/h)", "0"), new("Imperial (mph)", "1")], def: "1", plugin: "Dash Cam V", category: "Display"),
        Text("dashcam.unit", "Unit name", "plugins/DashCamV.ini", "UnitName",
            "Your name, shown on the dash cam overlay.",
            plugin: "Dash Cam V", category: "Identity", profile: ProfileField.OfficerName),
        Text("dashcam.department", "Department", "plugins/DashCamV.ini", "Department",
            "Your department, shown on the dash cam overlay.",
            plugin: "Dash Cam V", category: "Identity", profile: ProfileField.DepartmentName),
        Toggle("dashcam.bw", "Black and white filter", "plugins/DashCamV.ini", "BlackAndWhite",
            "Off gives a colour picture.",
            on: "true", off: "false", defaultOn: false, plugin: "Dash Cam V", category: "Display"),

        // ===== LIAR radar gun ==========================================
        Number("liar.x", "Readout position X", "plugins/LIAR.ini", "PositionX",
            "Horizontal position, kept clear of the postal code.", min: 0, max: 2000, step: 1, def: "612", plugin: "LIAR", category: "Layout"),
        Number("liar.y", "Readout position Y", "plugins/LIAR.ini", "PositionY",
            "Vertical position, kept clear of the status text.", min: 0, max: 1200, step: 1, def: "755", plugin: "LIAR", category: "Layout"),
        Number("liar.scale", "Readout scale", "plugins/LIAR.ini", "Scale",
            "Size of the readout.", min: 0, max: 100, step: 1, def: "71", plugin: "LIAR", category: "Layout"),
        Number("liar.volume", "Volume", "plugins/LIAR.ini", "Volume",
            "Beep volume of the radar gun.", min: 0, max: 10, step: 1, def: "5", plugin: "LIAR", category: "Audio"),
        Choice("liar.colour", "HUD colour", "plugins/LIAR.ini", "HUDColour",
            "Colour of the readout.",
            [new("Green", "0"), new("Red", "1")], def: "1", plugin: "LIAR", category: "Layout"),

        // ===== Speed Radar Lite ========================================
        Number("radar.threshold", "Initial speed threshold", "plugins/lspdfr/SpeedRadar.ini", "InitialSpeedThreshold",
            "The speed the radar starts flagging at.",
            min: 0, max: 200, step: 5, def: "55", unit: "mph", plugin: "Speed Radar", category: "Detection"),

        // ===== Riskier Traffic Stops ===================================
        Number("riskier.chance", "Risk chance", "plugins/RiskierTrafficStops.ini", "Chance",
            "How often a stop goes wrong, out of 100. Thirty is roughly one in three.",
            min: 0, max: 100, step: 5, def: "30", unit: "%", plugin: "Riskier Traffic Stops", category: "Behaviour"),

        // ===== Ultimate Backup =========================================
        Toggle("ub.code2", "Code-2 siren lights", "plugins/UltimateBackup.ini", "PerimetersCode2SirenLightsOn",
            "Whether perimeter units run lights on a code-2 response.",
            on: "YES", off: "NO", defaultOn: false, plugin: "Ultimate Backup", category: "Behaviour"),

        // ===== Simple Trainer ==========================================
        Number("trainer.spawnDriver", "Spawn-a-driver key", "TrainerV.ini", "SpawnADriverKey",
            "Set to 0 to disable — it defaults to a key you will be using constantly.",
            min: 0, max: 255, step: 1, def: "0", plugin: "Simple Trainer", category: "Keys"),
        Number("trainer.waypoint", "Add-waypoint key", "TrainerV.ini", "AddWaypointKey",
            "Set to 0 to disable — the default spawns hostile vehicles behind you.",
            min: 0, max: 255, step: 1, def: "0", plugin: "Simple Trainer", category: "Keys"),

        // ===== Callout Interface — the rest of the terminal ============
        Number("ci.plateX", "Plate position X", "plugins/LSPDFR/CalloutInterface.ini", "PlatePositionX",
            "Horizontal position of the plate readout.", min: 0, max: 2000, step: 1, def: "495", plugin: "Callout Interface", category: "Plate"),
        Number("ci.plateY", "Plate position Y", "plugins/LSPDFR/CalloutInterface.ini", "PlatePositionY",
            "Vertical position of the plate readout.", min: 0, max: 1200, step: 1, def: "907", plugin: "Callout Interface", category: "Plate"),
        Number("ci.plateScale", "Plate scale", "plugins/LSPDFR/CalloutInterface.ini", "PlateScale",
            "Size of the plate readout.", min: 0, max: 100, step: 1, def: "60", plugin: "Callout Interface", category: "Plate"),
        Text("ci.postalSet", "Postal code set", "plugins/LSPDFR/CalloutInterface.ini", "PostalCodeSet",
            "Which postal map the codes are read from.", plugin: "Callout Interface", category: "Postal", def: "virus_City"),
        Number("ci.mdtSound", "MDT sound", "plugins/LSPDFR/CalloutInterface.ini", "MDTSoundDisplay",
            "Which of the built-in terminal sounds plays.", min: 0, max: 10, step: 1, def: "4", plugin: "Callout Interface", category: "Terminal"),
        Toggle("ci.blipEnabled", "Show call blip", "plugins/LSPDFR/CalloutInterface.ini", "BlipEnabled",
            "Draws a map blip for the active call.", on: "True", off: "False", defaultOn: true, plugin: "Callout Interface", category: "Map"),
        Toggle("ci.autoTab", "Auto-tab the terminal", "plugins/LSPDFR/CalloutInterface.ini", "AutoTab",
            "Moves the terminal to the relevant tab for each call automatically.", on: "True", off: "False", defaultOn: true, plugin: "Callout Interface", category: "Terminal"),

        // ===== Grammar Police — layout and responses ==================
        Number("gp.statusX", "Status text X", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "StatusTextPositionX",
            "Horizontal position of the on-duty status text.", min: 0, max: 2000, step: 1, def: "489", plugin: "Grammar Police", category: "Layout"),
        Number("gp.statusY", "Status text Y", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "StatusTextPositionY",
            "Vertical position of the on-duty status text.", min: 0, max: 1200, step: 1, def: "980", plugin: "Grammar Police", category: "Layout"),
        Number("gp.statusScale", "Status text scale", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "StatusTextScale",
            "Size of the status text.", min: 0, max: 100, step: 1, def: "47", plugin: "Grammar Police", category: "Layout"),
        Number("gp.radialX", "Radial menu X", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "RadialPositionX",
            "Horizontal position of the radial voice menu.", min: 0, max: 2000, step: 1, def: "625", plugin: "Grammar Police", category: "Layout"),
        Number("gp.radialScale", "Radial menu scale", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "RadialScale",
            "Size of the radial voice menu.", min: 0, max: 100, step: 1, def: "43", plugin: "Grammar Police", category: "Layout"),
        Number("gp.radioY", "Radio position Y", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "RadioPositionY",
            "Vertical position of the radio panel.", min: 0, max: 1200, step: 1, def: "669", plugin: "Grammar Police", category: "Layout"),
        Toggle("gp.playerStatus", "Show player status", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "PlayerStatus",
            "Displays your on-duty status on screen.", on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Interface"),
        Toggle("gp.targetPlate", "Show target plate", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "ShowTargetPlate",
            "Shows the plate of the vehicle you are speaking about.", on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Interface"),
        Toggle("gp.pursuit", "Attempt to initiate pursuit", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "AttemptToInitiatePursuit",
            "Lets you call a pursuit by voice.", on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Voice"),
        Toggle("gp.generic", "Use generic responses", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "UseGenericResponse",
            "Falls back to a generic reply when dispatch does not recognise a phrase.", on: "True", off: "False", defaultOn: true, plugin: "Grammar Police", category: "Voice"),
        Toggle("gp.useNatives", "Use natives", "plugins/LSPDFR/GrammarPolice/custom/config.ini", "UseNatives",
            "A compatibility block the guide sets off. Leave it off unless a mod asks for it.", on: "True", off: "False", defaultOn: false, plugin: "Grammar Police", category: "Advanced"),

        // ===== Stop The Ped — transport =================================
        Toggle("stp.transportSiren", "Transport siren", "plugins/StopThePed.ini", "PrisonerTransportSiren",
            "Whether a called transport unit runs its siren.", on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Transport"),
        Toggle("stp.transportLights", "Transport lights", "plugins/StopThePed.ini", "PrisonerTransportLights",
            "Whether a called transport unit runs its lights.", on: "YES", off: "NO", defaultOn: false, plugin: "Stop The Ped", category: "Transport"),

        // ===== Simple HUD — clock and roads =============================
        Number("hud.roadScale", "Road scale", "scripts/SimpleHUD.ini", "RoadScale",
            "Size of the street name.", min: 0, max: 100, step: 1, def: "51", plugin: "Simple HUD", category: "Layout"),
        Number("hud.timeY", "Clock position Y", "scripts/SimpleHUD.ini", "TimePositionY",
            "Vertical position of the on-screen clock.", min: 0, max: 1200, step: 1, def: "912", plugin: "Simple HUD", category: "Clock"),
        Toggle("hud.timeEnabled", "Show clock", "scripts/SimpleHUD.ini", "TimeEnabled",
            "Displays the in-game time on the HUD.", on: "true", off: "false", defaultOn: true, plugin: "Simple HUD", category: "Clock"),

        // ===== Dash Cam V — the rest ====================================
        Choice("dashcam.date", "Date format", "plugins/DashCamV.ini", "DateFormat",
            "Order of day, month and year on the overlay.",
            [new("Day / Month / Year", "0"), new("Month / Day / Year", "1"), new("Year / Month / Day", "2")],
            def: "0", plugin: "Dash Cam V", category: "Display"),
        Text("dashcam.state", "State", "plugins/DashCamV.ini", "State",
            "The state shown on the overlay.", plugin: "Dash Cam V", category: "Identity", def: "San Andreas"),

        // ===== Heli Assistance ==========================================
        Text("heli.player", "Pilot name", "plugins/lspdfr/HeliAssistance.ini", "PlayerName",
            "Your name, spoken by the air unit.", plugin: "Heli Assistance", category: "Identity", profile: ProfileField.OfficerName),
        Text("heli.unit", "Air unit callsign", "plugins/lspdfr/HeliAssistance.ini", "UnitName",
            "The air unit's callsign.", plugin: "Heli Assistance", category: "Identity", profile: ProfileField.AirUnitCallsign),

        // ===== Radio Realism (Officer Porky) ============================
        Toggle("porky.street", "Street-detection notification", "plugins/lspdfr/OfficerPorky.ini", "DisplayStreetDetectionNotification",
            "Announces the street a stopped vehicle is on.", on: "True", off: "False", defaultOn: true, plugin: "Radio Realism", category: "Notifications"),
        Toggle("porky.pedId", "Auto ped ID check", "plugins/lspdfr/OfficerPorky.ini", "EnableAutoPedIDCheck",
            "Runs a records check on a stopped driver automatically.", on: "True", off: "False", defaultOn: true, plugin: "Radio Realism", category: "Notifications"),
        Toggle("porky.under60", "Alert on slow vehicles", "plugins/lspdfr/OfficerPorky.ini", "Under60Notification",
            "Off keeps alerts to speeders rather than every passing car.", on: "True", off: "False", defaultOn: false, plugin: "Radio Realism", category: "Notifications"),
    ];

    /// <summary>Every plugin that owns at least one setting, for the filter.</summary>
    public static IReadOnlyList<string> Plugins =>
        Settings.Select(s => s.Plugin).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

    private static ModSetting Toggle(
        string id, string name, string file, string key, string description,
        string on, string off, bool defaultOn, string plugin, string category) =>
        new()
        {
            Id = id, Name = name, Description = description, Plugin = plugin, Category = category,
            ConfigFile = file, ConfigKey = key, Kind = SettingKind.Toggle,
            OnLiteral = on, OffLiteral = off, Default = defaultOn ? on : off,
        };

    private static ModSetting Number(
        string id, string name, string file, string key, string description,
        double min, double max, double step, string def, string plugin, string category, string? unit = null) =>
        new()
        {
            Id = id, Name = name, Description = description, Plugin = plugin, Category = category,
            ConfigFile = file, ConfigKey = key, Kind = SettingKind.Number,
            Min = min, Max = max, Step = step, Default = def, Unit = unit,
        };

    private static ModSetting Choice(
        string id, string name, string file, string key, string description,
        IReadOnlyList<SettingOption> options, string def, string plugin, string category) =>
        new()
        {
            Id = id, Name = name, Description = description, Plugin = plugin, Category = category,
            ConfigFile = file, ConfigKey = key, Kind = SettingKind.Choice,
            Options = options, Default = def,
        };

    private static ModSetting Text(
        string id, string name, string file, string key, string description,
        string plugin, string category, ProfileField profile = ProfileField.None,
        bool quoted = false, string def = "") =>
        new()
        {
            Id = id, Name = name, Description = description, Plugin = plugin, Category = category,
            ConfigFile = file, ConfigKey = key, Kind = SettingKind.Text,
            Profile = profile, Quoted = quoted, Default = def,
        };
}
