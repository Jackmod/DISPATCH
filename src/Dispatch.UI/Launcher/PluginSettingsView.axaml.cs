using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The plugin settings section. Reads the current config values from the game
/// folder when it first appears, so the editors show what is really on disk.
/// </summary>
public partial class PluginSettingsView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public PluginSettingsView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PluginSettingsViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+F focuses search, matching the controls screen.
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
