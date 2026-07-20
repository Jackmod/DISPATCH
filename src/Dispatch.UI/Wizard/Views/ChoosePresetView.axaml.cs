using Avalonia.Controls;
using Avalonia.Interactivity;
using Dispatch.UI.Wizard.Steps;

namespace Dispatch.UI.Wizard.Views;

/// <summary>Screen 4. Three setups as live preview surfaces.</summary>
public partial class ChoosePresetView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public ChoosePresetView() => InitializeComponent();

    /// <summary>Opens or closes the per-mod customise overlay.</summary>
    private void OnToggleCustomize(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChoosePresetStep step)
        {
            step.ToggleCustomize();
        }
    }
}
