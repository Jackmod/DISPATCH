using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Profiles;

namespace Dispatch.UI.Launcher;

/// <summary>One destination in the left rail.</summary>
/// <param name="Key">Stable identifier.</param>
/// <param name="Label">What the rail shows.</param>
/// <param name="IconKey">Resource key of the glyph geometry.</param>
public sealed record NavItem(string Key, string Label, string IconKey)
{
    /// <summary>
    /// The glyph, resolved from application resources.
    /// </summary>
    /// <remarks>
    /// Resolved on the model rather than through a value converter because the
    /// icon set is a flat resource lookup, and a converter would add a class
    /// whose entire job is a dictionary read the model can do directly.
    /// </remarks>
    public Geometry? Icon =>
        Application.Current?.TryGetResource(IconKey, null, out var value) == true
            ? value as Geometry
            : null;
}

/// <summary>One step in the OpenIV walkthrough.</summary>
/// <param name="Number">Its position in the sequence.</param>
/// <param name="Title">What this step achieves.</param>
/// <param name="Detail">How to do it, in plain words.</param>
/// <param name="Path">An exact navigation path to show in mono, if the step has one.</param>
public sealed record OpenIvStep(int Number, string Title, string Detail, string? Path)
{
    /// <summary>Whether there is a path to show.</summary>
    public bool HasPath => !string.IsNullOrWhiteSpace(Path);
}

/// <summary>
/// The launcher shell: the left rail, the officer header, and which section is
/// showing.
/// </summary>
/// <remarks>
/// The shell owns navigation and the active officer; the sections own their
/// own content. Routing is a matter of changing <see cref="Current"/>, which a
/// TransitioningContentControl turns into a page transition, so adding a
/// section is a nav item and a template rather than any new plumbing.
/// </remarks>
public sealed partial class LauncherViewModel : ObservableObject
{
    [ObservableProperty]
    private NavItem _current;

    [ObservableProperty]
    private object _currentContent;

    [ObservableProperty]
    private OfficerProfile? _officer;

    /// <summary>The version staged and waiting to apply on next restart, or null.</summary>
    [ObservableProperty]
    private string? _stagedUpdateVersion;

    /// <summary>Whether the "restart to update" note is showing.</summary>
    public bool HasStagedUpdate => !string.IsNullOrWhiteSpace(StagedUpdateVersion);

    partial void OnStagedUpdateVersionChanged(string? value) => OnPropertyChanged(nameof(HasStagedUpdate));

    private void OnUpdateStaged(object? sender, string version) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StagedUpdateVersion = version);

    /// <summary>Dismisses the update note; the update still applies on next restart.</summary>
    [RelayCommand]
    private void DismissUpdate() => StagedUpdateVersion = null;

    private readonly Core.Detection.IGameBuildWatch? _buildWatch;
    private readonly string? _gamePath;
    private readonly Core.Platform.IGameLauncher? _launcher;
    private readonly Core.Platform.AppUpdateSignal? _updateSignal;

    // Screens are built on first visit, not up front, so opening the launcher
    // constructs only the dashboard rather than six view models — each of which
    // reads the catalogue, the profile or the disk. Every one is a singleton once
    // made, so its load-once state and staged edits survive tab switches.
    private DashboardViewModel? _dashboard;
    private ProfileViewModel? _profile;
    private ControlsViewModel? _controls;
    private PluginSettingsViewModel? _pluginSettings;
    private ModsViewModel? _mods;
    private SettingsViewModel? _settings;

    /// <summary>Constructs the shell. Screens are created lazily on first navigation.</summary>
    /// <param name="officer">The officer on duty.</param>
    /// <param name="buildWatch">Watches for a game update that outdated the script hooks.</param>
    /// <param name="gamePath">The installed game folder.</param>
    /// <param name="launcher">Starts RagePluginHook when going on duty.</param>
    public LauncherViewModel(
        OfficerProfile? officer = null,
        Core.Detection.IGameBuildWatch? buildWatch = null,
        string? gamePath = null,
        Core.Platform.IGameLauncher? launcher = null,
        Core.Platform.AppUpdateSignal? updateSignal = null)
    {
        _officer = officer;
        _buildWatch = buildWatch;
        _gamePath = gamePath;
        _launcher = launcher;
        _updateSignal = updateSignal;

        // A newer version may already be staged (the check runs at startup), or it may
        // stage while the launcher is open — handle both so the note never gets missed.
        if (updateSignal is not null)
        {
            StagedUpdateVersion = updateSignal.StagedVersion;
            updateSignal.UpdateStaged += OnUpdateStaged;
        }

        Cleaner = new CleanerViewModel();

        Palette = new CommandPaletteViewModel();
        Palette.Chosen += OnPaletteChosen;

        // Keybinds and plugin settings are no longer top-level rail items: they
        // live inside Settings, which is now a hub. Profile moved to the top-right
        // header. The rail is the three places you actually go.
        Items =
        [
            new NavItem("dashboard", "Dashboard", "IconDashboard"),
            new NavItem("mods", "Mods", "IconMods"),
            new NavItem("settings", "Settings", "IconSettings"),
        ];

        _current = Items[0];
        _currentContent = Dashboard;
    }

    private DashboardViewModel Dashboard =>
        _dashboard ??= new DashboardViewModel(Officer, _buildWatch, _gamePath, _launcher);

    private ProfileViewModel Profile =>
        _profile ??= new ProfileViewModel(Officer, _gamePath);

    private ControlsViewModel Controls =>
        _controls ??= new ControlsViewModel(gamePath: _gamePath, officer: Officer);

    private PluginSettingsViewModel PluginSettings =>
        _pluginSettings ??= new PluginSettingsViewModel(gamePath: _gamePath, officer: Officer);

    private ModsViewModel Mods =>
        _mods ??= new ModsViewModel(_gamePath);

    private SettingsViewModel Settings => _settings ??= CreateSettings();

    private SettingsViewModel CreateSettings()
    {
        var settings = new SettingsViewModel(Officer, _gamePath);

        // Switching officer in Settings rewrites who is on duty everywhere: the header
        // updates, and the screens that bake the officer in are dropped so they rebuild
        // with the new identity on the next visit.
        settings.ActiveOfficerChanged += (_, officer) =>
        {
            Officer = officer;
            _dashboard = null;
            _profile = null;
            _controls = null;
            _pluginSettings = null;
        };

        return settings;
    }

    /// <summary>The rail destinations, in order.</summary>
    public IReadOnlyList<NavItem> Items { get; }

    /// <summary>The Ctrl+K command palette.</summary>
    public CommandPaletteViewModel Palette { get; }

    /// <summary>The Clean GTA folder modal.</summary>
    public CleanerViewModel Cleaner { get; }

    /// <summary>Whether the cleaner modal is showing.</summary>
    [ObservableProperty]
    private bool _cleanerOpen;

    /// <summary>Whether the OpenIV step-by-step guide is showing.</summary>
    [ObservableProperty]
    private bool _openIvHelpOpen;

    /// <summary>The OpenIV walkthrough, one card per step.</summary>
    public IReadOnlyList<OpenIvStep> OpenIvSteps { get; } =
    [
        new(1, "Install OpenIV",
            "Run the installer, accept the terms, and continue. The path is fixed to your C: drive — that is normal. Uncheck \"Run OpenIV after installation\" so you can point it at your game yourself.",
            null),
        new(2, "Point it at your game",
            "Open OpenIV and choose \"Windows\" for Grand Theft Auto V. Browse to your game folder, select it once, and click Select Folder. A green tick confirming it found GTA5.exe means you are in the right place.",
            null),
        new(3, "Turn on Edit mode",
            "Tools → Options → General → set Default Work Mode to Edit, then close. Click the blue Edit Mode banner. From now on OpenIV opens ready to edit.",
            "Tools ▸ Options ▸ General ▸ Edit"),
        new(4, "Install the ASI plugins",
            "ASI Manager → install OpenIV.ASI (leave both boxes ticked) and OpenCamera. A mods folder now exists, currently empty.",
            "ASI Manager ▸ OpenIV.ASI ▸ Install"),
        new(5, "Build the mods folder",
            "Close OpenIV, open your game folder, hold Ctrl and select both the update and x64 folders, copy them, and paste into the mods folder. This gives OpenIV a safe layer so it never touches the original game archives. It takes a few minutes.",
            "update + x64  ▸  mods\\"),
        new(6, "Import the LIAR radar-gun textures",
            "In edit mode, open weapons.rpf and drag in the Vintage Pistol files (not the readme). Paste a filename into the search box to jump straight to them.",
            "mods\\update\\x64\\dlcpacks\\patchday8ng\\dlc.rpf\\x64\\models\\cdimages\\weapons.rpf"),
        new(7, "Import the Grammar Police CB radio",
            "Open v_minigame.rpf and drag in both texture files. If x64c.rpf is not in mods yet, right-click it in the left column and Copy to mods folder first.",
            "mods\\x64c.rpf\\levels\\gta5\\props\\lev_des\\v_minigame.rpf"),
        new(8, "Import the Simple HUD menu texture",
            "Open script_txds.rpf and drag in simpleMenu.ytd from the archive's textures folder. Then File → Close All Archives and close OpenIV.",
            "mods\\update\\update.rpf\\x64\\textures\\script_txds.rpf"),
    ];

    /// <summary>Opens the cleaner against the active game folder.</summary>
    public async Task OpenCleanerAsync()
    {
        CleanerOpen = true;

        // The installed game folder from the profile — not Cleaner.GamePath, which
        // is empty until a scan sets it. Without a folder the modal still opens and
        // its empty state explains there is nothing to scan.
        if (!string.IsNullOrWhiteSpace(_gamePath))
        {
            await Cleaner.ScanAsync(_gamePath).ConfigureAwait(true);
        }
    }

    /// <summary>Closes the cleaner modal.</summary>
    public void CloseCleaner() => CleanerOpen = false;

    /// <summary>Opens the OpenIV walkthrough.</summary>
    [RelayCommand]
    private void OpenOpenIvHelp() => OpenIvHelpOpen = true;

    /// <summary>Closes the OpenIV walkthrough.</summary>
    [RelayCommand]
    private void CloseOpenIvHelp() => OpenIvHelpOpen = false;

    /// <summary>
    /// Development only: opens the cleaner over a throwaway fixture folder so
    /// the modal can be demonstrated without a real game install.
    /// </summary>
    public async Task OpenCleanerOnFixtureAsync()
    {
        var fixture = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dispatch-cleaner-demo");
        System.IO.Directory.CreateDirectory(fixture);

        // Stock files (never offered), mod files (known and likely), and an
        // unknown, so all three tiers populate.
        void Write(string rel, string content)
        {
            var full = System.IO.Path.Combine(fixture, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, content);
        }

        Write("GTA5.exe", "stock");
        Write("dinput8.dll", new string('x', 4200));
        Write("ScriptHookV.dll", new string('x', 780000));
        Write("plugins/StopThePed.dll", new string('x', 240000));
        Write("plugins/LSPDFR/GrammarPolice.dll", new string('x', 96000));
        Write("scripts/LemonUI.SHVDN3.dll", new string('x', 48000));
        Write("holiday-screenshot.png", new string('x', 1_400_000));
        Write("notes.txt", "my notes");

        CleanerOpen = true;
        await Cleaner.ScanAsync(fixture).ConfigureAwait(true);
    }

    /// <summary>The officer callsign shown under the badge, or a placeholder.</summary>
    public string CallsignLabel => Officer?.Callsign ?? "No officer";

    /// <summary>The officer name, or a placeholder.</summary>
    public string OfficerName => Officer?.Name ?? "Set up an officer";

    /// <summary>Two-letter initials for the top-right profile widget.</summary>
    public string Initials
    {
        get
        {
            var name = Officer?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return "★";
            }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    /// <summary>The title shown in the top bar for the current place.</summary>
    public string HeaderTitle => Current?.Label ?? "Dashboard";

    /// <summary>Navigates to a section.</summary>
    /// <remarks>
    /// The rail binds <c>SelectedItem</c> to <see cref="Current"/> two-way, so a click
    /// updates <see cref="Current"/> before this command runs. Guarding on
    /// <c>item.Key == Current.Key</c> therefore always short-circuited — the header
    /// changed but the page never did. So the target content is resolved from the
    /// clicked item directly and always assigned; setting it to the same cached view
    /// instance is a no-op, so there is no needless rebuild.
    /// </remarks>
    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        Current = item;
        CurrentContent = item.Key switch
        {
            "dashboard" => Dashboard,
            "mods" => Mods,
            "settings" => Settings,
            _ => Dashboard,
        };
    }

    /// <summary>Opens the profile / stats page from the top-right header.</summary>
    [RelayCommand]
    private void ShowProfile() => CurrentContent = Profile;

    /// <summary>Jumps to a section by key, for the command palette.</summary>
    public void GoTo(string key)
    {
        if (key == "profile")
        {
            ShowProfile();
            return;
        }

        Navigate(Items.FirstOrDefault(i => i.Key == key));
    }

    /// <summary>Opens the Settings hub on the keybinds section with an action searched.</summary>
    private void GoToKeybinds(string? search)
    {
        GoTo("settings");
        Settings.Section = "keybinds";
        if (!string.IsNullOrWhiteSpace(search))
        {
            Settings.Keybinds.Search = search;
        }
    }

    private void OnPaletteChosen(object? sender, Dispatch.Core.Palette.PaletteEntry entry)
    {
        switch (entry.Action)
        {
            case Dispatch.Core.Palette.PaletteAction.Navigate:
                GoTo(entry.Target);
                break;

            case Dispatch.Core.Palette.PaletteAction.EditBinding:
                // Keybinds live in the Settings hub now, so drop the user there on
                // the keybinds section with that action searched.
                GoToKeybinds(entry.Title);
                break;

            case Dispatch.Core.Palette.PaletteAction.Run when entry.Target == "clean":
                _ = OpenCleanerAsync();
                break;

            case Dispatch.Core.Palette.PaletteAction.Run when entry.Target == "controls":
                GoToKeybinds(null);
                break;

            // Other commands (clean, audit, on duty) hang off features not yet
            // built; navigating is the safe no-op until they are.
            default:
                break;
        }
    }

    partial void OnOfficerChanged(OfficerProfile? value)
    {
        OnPropertyChanged(nameof(CallsignLabel));
        OnPropertyChanged(nameof(OfficerName));
        OnPropertyChanged(nameof(Initials));
    }

    partial void OnCurrentChanged(NavItem value) => OnPropertyChanged(nameof(HeaderTitle));
}
