using Avalonia.Controls;

namespace Dispatch.UI.Launcher;

/// <summary>The launcher shell. Code-behind forwards rail selection to the view model.</summary>
public partial class LauncherView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public LauncherView() => InitializeComponent();

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is LauncherViewModel model && e.AddedItems is [NavItem item, ..])
        {
            model.NavigateCommand.Execute(item);
        }
    }
}
