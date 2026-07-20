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

    private readonly DashboardViewModel _dashboard;
    private readonly ControlsViewModel _controls;
    private readonly ModsViewModel _mods;
    private readonly SettingsViewModel _settings;

    /// <summary>Constructs the shell with placeholder content for each section.</summary>
    public LauncherViewModel(OfficerProfile? officer = null)
    {
        _officer = officer;
        _dashboard = new DashboardViewModel(officer);
        _controls = new ControlsViewModel();
        _mods = new ModsViewModel();
        _settings = new SettingsViewModel();

        Cleaner = new CleanerViewModel();

        Palette = new CommandPaletteViewModel();
        Palette.Chosen += OnPaletteChosen;

        Items =
        [
            new NavItem("dashboard", "Dashboard", "IconDashboard"),
            new NavItem("mods", "Mods", "IconMods"),
            new NavItem("controls", "Controls", "IconControls"),
            new NavItem("settings", "Settings", "IconSettings"),
        ];

        _current = Items[0];
        _currentContent = _dashboard;
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

    /// <summary>Opens the cleaner against the active game folder.</summary>
    public async Task OpenCleanerAsync()
    {
        CleanerOpen = true;
        var gamePath = Officer is null ? string.Empty : Cleaner.GamePath;

        // Nothing to scan without a folder; the modal still opens so its empty
        // state can explain that. A real game path arrives from the profile.
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            await Cleaner.ScanAsync(gamePath).ConfigureAwait(true);
        }
    }

    /// <summary>Closes the cleaner modal.</summary>
    public void CloseCleaner() => CleanerOpen = false;

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

    /// <summary>Navigates to a section.</summary>
    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is null || item.Key == Current.Key)
        {
            return;
        }

        Current = item;
        CurrentContent = item.Key switch
        {
            "dashboard" => _dashboard,
            "mods" => _mods,
            "controls" => _controls,
            "settings" => _settings,
            _ => _dashboard,
        };
    }

    /// <summary>Jumps to a section by key, for the command palette.</summary>
    public void GoTo(string key) => Navigate(Items.FirstOrDefault(i => i.Key == key));

    private void OnPaletteChosen(object? sender, Dispatch.Core.Palette.PaletteEntry entry)
    {
        switch (entry.Action)
        {
            case Dispatch.Core.Palette.PaletteAction.Navigate:
                GoTo(entry.Target);
                break;

            case Dispatch.Core.Palette.PaletteAction.EditBinding:
                // Land on the controls screen with that action searched, so the
                // palette drops the user exactly on the row they named.
                GoTo("controls");
                _controls.Search = entry.Title;
                break;

            case Dispatch.Core.Palette.PaletteAction.Run when entry.Target == "clean":
                _ = OpenCleanerAsync();
                break;

            case Dispatch.Core.Palette.PaletteAction.Run when entry.Target == "controls":
                GoTo("controls");
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
    }
}
