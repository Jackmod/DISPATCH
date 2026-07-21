namespace Dispatch.Core.Controls;

/// <summary>
/// Every action Dispatch knows how to bind, and the two schemes that ship.
/// </summary>
/// <remarks>
/// A declarative table rather than a wall of branches. Adding a mod's binds
/// later should be new rows, never new code.
///
/// <para>
/// Bindings here come from the tutorial the spec is built on. Where two mods
/// disagree — Grammar Police shipping its voice interface on F3, the same key
/// as Simple Trainer — the tutorial moves the newcomer aside (to F8). That
/// conflict is real and is the sort of thing this screen exists to catch.
/// </para>
/// </remarks>
public static class ControlCatalogue
{
    /// <summary>Every keyboard and controller action, in display order.</summary>
    public static readonly IReadOnlyList<GameAction> Actions =
    [
        // ===== LSPDFR core (lspdfr/keys.ini) ===========================
        Action("lspdfr.duty", "Go on or off duty", "Report for duty, or end your shift.",
            "LSPDFR", "Duty", "lspdfr/keys.ini", "TOGGLE_DUTY_Key"),
        Action("lspdfr.arrest", "Arrest suspect", "Place the suspect you are aiming at under arrest.",
            "LSPDFR", "Suspects", "lspdfr/keys.ini", "PERFORM_ARREST_Key"),
        Action("lspdfr.stop", "Start traffic stop",
            "Pull over the vehicle ahead. The de facto standard for this is Left Shift; changing it will surprise anyone who has played before.",
            "LSPDFR", "Traffic", "lspdfr/keys.ini", "TRAFFICSTOP_START_Key"),
        Action("lspdfr.interact", "Interact", "Talk to the person you are looking at.",
            "LSPDFR", "Suspects", "lspdfr/keys.ini", "TRAFFICSTOP_INTERACT_Key"),
        Action("lspdfr.backup", "Request backup", "Open the backup menu.",
            "LSPDFR", "Dispatch", "lspdfr/keys.ini", "BACKUP_MENU_Key"),
        Action("lspdfr.computer", "Police computer", "Open the in-car computer.",
            "LSPDFR", "Dispatch", "lspdfr/keys.ini", "TOGGLE_POLICECOMPUTER_Key"),

        // ===== Stop The Ped (plugins/LSPDFR/StopThePed.ini) ============
        // WinForms key names — the file writes D8-style number-row keys and named
        // keys like Back/Enter, not the bare digit.
        Action("stp.patdown", "Pat down suspect",
            "Search the person you are aiming at for weapons and contraband.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "SearchKey"),
        Action("stp.transport", "Transport suspect", "Put an arrested suspect into a vehicle.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "CallTransportKey"),
        Action("stp.tackle", "Tackle", "Tackle a fleeing suspect.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "TacklePedKey"),
        Action("stp.sprint", "Sprint boost", "Run faster while chasing on foot.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "SprintBoostKey"),

        // ===== Callout Interface =======================================
        Action("ci.menu", "Callout menu", "Open the Callout Interface menu.",
            "Callout Interface", "Dispatch", "plugins/LSPDFR/CalloutInterface.ini", "CalloutMenuKey"),
        Action("ci.terminal", "MDT terminal", "Open the mobile data terminal.",
            "Callout Interface", "Dispatch", "plugins/LSPDFR/CalloutInterface.ini", "ToggleTerminalKey"),
        Action("ci.alpr", "Run plate (ALPR)", "Read the plate of the vehicle you are looking at.",
            "Callout Interface", "Traffic", "plugins/LSPDFR/CalloutInterface.ini", "ToggleALPRKey"),

        // ===== Grammar Police (GrammarPolice/default.ini) ==============
        // Its modifier fields drop the trailing "Key" (InterfaceModifier).
        Action("gp.interface", "Voice interface", "Open the voice control interface.",
            "Grammar Police", "Voice", "plugins/LSPDFR/GrammarPolice/default.ini", "InterfaceKey"),
        Action("gp.settings", "Voice settings", "Open Grammar Police settings.",
            "Grammar Police", "Voice", "plugins/LSPDFR/GrammarPolice/default.ini", "SettingsKey"),
        Action("gp.radio", "Push to talk", "Hold to speak to dispatch.",
            "Grammar Police", "Voice", "plugins/LSPDFR/GrammarPolice/default.ini", "RadioKey"),

        // ===== Charges and Citations (CompuLite.ini) ===================
        Action("compulite.open", "Charges menu", "Open the charges and citations menu.",
            "Charges & Citations", "Suspects", "plugins/LSPDFR/CompuLite.ini", "OpenComputerKey"),
        Action("compulite.citation", "Write citation", "Issue a citation to the person you are dealing with.",
            "Charges & Citations", "Traffic", "plugins/LSPDFR/CompuLite.ini", "GiveCitationKey"),

        // ===== Everything else =========================================
        Action("backup.menu", "Backup menu", "Call a specific unit to your location.",
            "Ultimate Backup", "Dispatch", "plugins/LSPDFR/UltimateBackup.ini", "ToggleMenuKey"),
        Action("hud.toggle", "Toggle HUD", "Show or hide the on-screen display.",
            "Simple HUD", "Interface", "SimpleHUD.ini", "ToggleKey"),
        Action("dashcam.toggle", "Toggle dash cam", "Turn the dash cam overlay on or off.",
            "Dash Cam V", "Interface", "plugins/DashCamV.ini", "RemoteToggleKey"),
        Action("spotlight.toggle", "Toggle spotlight", "Turn the vehicle spotlight on or off.",
            "Spotlight", "Vehicle", "plugins/spotlight_resources/General.ini", "Toggle"),
        Action("spotlight.editor", "Spotlight editor", "Adjust spotlight position and angle.",
            "Spotlight", "Vehicle", "plugins/spotlight_resources/General.ini", "EditorKey"),
        Action("restrain.toggle", "Restrain suspect", "Cuff or uncuff the person you are dealing with.",
            "Restrain", "Suspects", "plugins/Restrain The Deceased.ini", "RestrainKey"),
        Action("baitcar.menu", "Bait car menu", "Deploy or manage a bait car.",
            "Bait Car", "Vehicle", "plugins/LSPDFR/BaitCar.ini", "MenuKey"),
        Action("effects.menu", "Immersive effects", "Open the immersive effects menu.",
            "Immersive Effects", "Interface", "plugins/Immersive Effects.ini", "MenuKey"),
        Action("liar.menu", "Radar gun", "Raise the radar gun.",
            "LIAR", "Traffic", "plugins/LSPDFR/LidarGun.ini", "LidarKey"),
        Action("radar.increase", "Radar threshold up", "Raise the speed the radar flags.",
            "Speed Radar", "Traffic", "plugins/LSPDFR/SpeedRadarLite.ini", "IncreaseThreashold"),
        Action("radar.decrease", "Radar threshold down", "Lower the speed the radar flags.",
            "Speed Radar", "Traffic", "plugins/LSPDFR/SpeedRadarLite.ini", "DecreaseThreasholdKey"),
        Action("vehicle.delete", "Delete vehicle", "Remove the vehicle you are looking at.",
            "RPH Delete Vehicle", "Vehicle", "plugins/RPHDeleteVehicle.ini", "DeleteKey"),
        Action("screenshot.take", "Take screenshot", "Capture the current frame.",
            "In-Game Screenshot", "Interface", "scripts/InGameScreenshot.ini", "ScreenshotKey"),
        Action("skin.menu", "Skin control", "Change your uniform.",
            "Skin Control", "Interface", "SkinControl.ini", "Hotkey"),
        Action("clipboard.open", "Open clipboard", "Open the notes clipboard.",
            "Clipboard", "Interface", "plugins/LSPDFR/Clipboard.ini", "ClipboardKey"),

        // ===== Controller (lspdfr/keys.ini + StopThePed.ini) ===========
        Action("pad.stop", "Start traffic stop", "Pull over the vehicle ahead.",
            "LSPDFR", "Traffic", "lspdfr/keys.ini", "TRAFFICSTOP_START_ControllerKey",
            KeyDialect.Controller, InputDevice.Controller),
        Action("pad.interact", "Interact", "Talk to the person you are looking at.",
            "LSPDFR", "Suspects", "lspdfr/keys.ini", "TRAFFICSTOP_INTERACT_ControllerKey",
            KeyDialect.Controller, InputDevice.Controller),
        Action("pad.backup", "Request backup", "Open the backup menu.",
            "LSPDFR", "Dispatch", "lspdfr/keys.ini", "BACKUP_MENU_ControllerKey",
            KeyDialect.Controller, InputDevice.Controller),
        Action("pad.pursuit", "Pursuit menu", "Open the pursuit menu.",
            "LSPDFR", "Dispatch", "lspdfr/keys.ini", "PURSUIT_MENU_ControllerKey",
            KeyDialect.Controller, InputDevice.Controller),
        Action("pad.tackle", "Tackle", "Tackle a fleeing suspect.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "TacklePedButton",
            KeyDialect.Controller, InputDevice.Controller),
        Action("pad.sprint", "Sprint boost", "Run faster while chasing on foot.",
            "Stop The Ped", "Suspects", "plugins/LSPDFR/StopThePed.ini", "SprintBoostButton",
            KeyDialect.Controller, InputDevice.Controller),
    ];

    /// <summary>
    /// The conflict-free scheme, and the one preselected in the wizard.
    /// </summary>
    /// <remarks>
    /// Grammar Police's interface key is F8 here, matching the tutorial's
    /// on-screen config. Its shipped default (F3) collides with Simple Trainer;
    /// F8 is free once Callout's menu moves to F10 and Skin Control moves to Tab.
    /// Its settings key stays on Left Control + F7. (F4 — the RagePluginHook
    /// console — is never used by Grammar Police; the old "F4" was a guide typo.)
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, KeyBinding> Suggested =
        new Dictionary<string, KeyBinding>(StringComparer.Ordinal)
        {
            ["lspdfr.duty"] = Bind("F5"),
            ["lspdfr.arrest"] = Bind("E"),
            ["lspdfr.stop"] = Bind("LShiftKey"),
            ["lspdfr.interact"] = Bind("Y"),
            ["lspdfr.backup"] = Bind("B", KeyModifier.Shift),
            ["lspdfr.computer"] = Bind("F6"),

            ["stp.patdown"] = Bind("F9"),
            ["stp.transport"] = Bind("D9"),
            ["stp.tackle"] = Bind("X"),
            ["stp.sprint"] = Bind("A", KeyModifier.Shift),

            ["ci.menu"] = Bind("F10"),
            ["ci.terminal"] = Bind("NumPad7"),
            ["ci.alpr"] = Bind("D", KeyModifier.Control),

            ["gp.interface"] = Bind("F8"),
            ["gp.settings"] = Bind("F7", KeyModifier.Control),
            ["gp.radio"] = Bind("O", KeyModifier.Control),

            ["compulite.open"] = Bind("X", KeyModifier.Control),
            ["compulite.citation"] = Bind("X", KeyModifier.Shift),

            ["backup.menu"] = Bind("U"),
            ["hud.toggle"] = Bind("B"),
            ["dashcam.toggle"] = Bind("I"),
            ["spotlight.toggle"] = Bind("S", KeyModifier.Shift),
            ["spotlight.editor"] = Bind("F6", KeyModifier.Shift),
            ["restrain.toggle"] = Bind("E", KeyModifier.Shift),
            ["baitcar.menu"] = Bind("F11"),
            ["effects.menu"] = Bind("F2"),
            ["liar.menu"] = Bind("NumPad1"),
            ["radar.increase"] = Bind("I", KeyModifier.Shift),
            ["radar.decrease"] = Bind("O", KeyModifier.Shift),
            ["vehicle.delete"] = Bind("D", KeyModifier.Shift),
            ["screenshot.take"] = Bind("K"),
            ["skin.menu"] = Bind("D9", KeyModifier.Control),
            ["clipboard.open"] = Bind("T", KeyModifier.Control),

            ["pad.stop"] = Bind("DPadRight"),
            ["pad.interact"] = Bind("DPadLeft"),
            ["pad.backup"] = Bind("DPadUp"),
            ["pad.pursuit"] = Bind("DPadDown"),
            ["pad.tackle"] = Bind("PadX"),
            ["pad.sprint"] = Bind("PadA"),
        };

    /// <summary>
    /// Every mod's factory binds, exactly as its ini files arrive.
    /// </summary>
    /// <remarks>
    /// Kept as a reference, and honest about containing conflicts — several
    /// mods ship claiming the same keys, which is the situation the Suggested
    /// scheme exists to resolve. Offering this without saying so would be
    /// setting people up for the failure the app is meant to prevent.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, KeyBinding> Factory =
        new Dictionary<string, KeyBinding>(StringComparer.Ordinal)
        {
            ["lspdfr.duty"] = Bind("F5"),
            ["lspdfr.arrest"] = Bind("E"),
            ["lspdfr.stop"] = Bind("LShiftKey"),
            ["lspdfr.interact"] = Bind("Y"),
            ["lspdfr.backup"] = Bind("B"),
            ["lspdfr.computer"] = Bind("F6"),

            ["stp.patdown"] = Bind("F9"),
            ["stp.transport"] = Bind("D9"),
            ["stp.tackle"] = Bind("X"),
            ["stp.sprint"] = Bind("A"),

            ["ci.menu"] = Bind("F10"),
            ["ci.terminal"] = Bind("NumPad7"),
            ["ci.alpr"] = Bind("D"),

            // As shipped: collides with Simple Trainer (both F3).
            ["gp.interface"] = Bind("F3"),
            ["gp.settings"] = Bind("F7"),
            ["gp.radio"] = Bind("O", KeyModifier.Control),

            // As shipped: collides with Stop The Ped's tackle.
            ["compulite.open"] = Bind("X"),
            ["compulite.citation"] = Bind("X", KeyModifier.Shift),

            ["backup.menu"] = Bind("U"),

            // As shipped: collides with LSPDFR's backup menu.
            ["hud.toggle"] = Bind("B"),
            ["dashcam.toggle"] = Bind("I"),
            ["spotlight.toggle"] = Bind("S"),
            ["spotlight.editor"] = Bind("F6"),
            ["restrain.toggle"] = Bind("E"),
            ["baitcar.menu"] = Bind("F11"),
            ["effects.menu"] = Bind("F2"),
            ["liar.menu"] = Bind("NumPad1"),

            // As shipped: collides with the dash cam toggle.
            ["radar.increase"] = Bind("I"),
            ["radar.decrease"] = Bind("O"),
            ["vehicle.delete"] = Bind("D", KeyModifier.Shift),
            ["screenshot.take"] = Bind("K"),
            ["skin.menu"] = Bind("D9"),
            ["clipboard.open"] = Bind("T", KeyModifier.Control),

            ["pad.stop"] = Bind("DPadRight"),
            ["pad.interact"] = Bind("DPadLeft"),
            ["pad.backup"] = Bind("DPadUp"),
            ["pad.pursuit"] = Bind("DPadDown"),
            ["pad.tackle"] = Bind("PadX"),
            ["pad.sprint"] = Bind("PadA"),
        };

    /// <summary>Every category, for the filter chips.</summary>
    public static IReadOnlyList<string> Categories =>
        Actions.Select(a => a.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>Every plugin that owns at least one action.</summary>
    public static IReadOnlyList<string> Plugins =>
        Actions.Select(a => a.Plugin).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>Pairs the catalogue with a scheme, ready for conflict detection.</summary>
    public static IReadOnlyList<BoundAction> Bind(IReadOnlyDictionary<string, KeyBinding> scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        return Actions
            .Select(action => new BoundAction(
                action,
                scheme.TryGetValue(action.Id, out var binding) ? binding : KeyBinding.Unbound))
            .ToList();
    }

    private static GameAction Action(
        string id,
        string name,
        string description,
        string plugin,
        string category,
        string configFile,
        string configKey,
        KeyDialect dialect = KeyDialect.WinForms,
        InputDevice device = InputDevice.Keyboard) =>
        new(id, name, description, plugin, category, device, configFile, configKey, dialect);

    private static KeyBinding Bind(string token, KeyModifier modifier = KeyModifier.None) =>
        new(KeyTokens.Parse(token), modifier);
}
