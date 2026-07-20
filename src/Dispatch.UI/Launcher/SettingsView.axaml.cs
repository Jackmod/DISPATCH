using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dispatch.UI.Launcher;

/// <summary>The settings section: appearance, officer, folders, quarantine, about.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }
}
