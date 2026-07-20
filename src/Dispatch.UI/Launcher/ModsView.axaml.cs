using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dispatch.UI.Launcher;

/// <summary>The mods section: the installed list, its state on disk, and reversible removal.</summary>
public partial class ModsView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public ModsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ModsViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }
}
