using Avalonia.Controls;

namespace Dispatch.UI.Launcher;

/// <summary>The dashboard section: officer hero and status grid.</summary>
public partial class DashboardView : UserControl
{
    // The one manual step in the fix: Script Hook V is hosted on dev-c.com, not a
    // link Dispatch can follow. Script Hook V .NET re-installs from GitHub on the
    // next setup run; this opens the page for the base build to match.
    private static readonly Uri ScriptHookVPage = new("https://www.dev-c.com/gtav/scripthookv/");

    /// <summary>Constructs the view.</summary>
    public DashboardView()
    {
        InitializeComponent();
        UpdateScriptHookButton.Click += (_, _) => TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(ScriptHookVPage);
        GoOnDutyButton.Click += OnGoOnDuty;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }

    private void OnGoOnDuty(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Best effort: starts RagePluginHook when it is installed and reachable.
        (DataContext as DashboardViewModel)?.GoOnDuty();
    }
}
