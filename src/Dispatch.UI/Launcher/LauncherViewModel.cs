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

    partial void OnOfficerChanged(OfficerProfile? value)
    {
        OnPropertyChanged(nameof(CallsignLabel));
        OnPropertyChanged(nameof(OfficerName));
    }
}
