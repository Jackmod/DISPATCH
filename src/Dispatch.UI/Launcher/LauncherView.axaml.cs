using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Dispatch.UI.Launcher;

/// <summary>The launcher shell. Code-behind wires rail selection and the palette.</summary>
public partial class LauncherView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public LauncherView()
    {
        InitializeComponent();

        // Tunnelling so Ctrl+K and the palette's own navigation keys are seen
        // before a focused text box or the list consumes them.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // A double-click or tap on a result chooses it, the same as Enter.
        PaletteResults.DoubleTapped += (_, _) => Palette?.ChooseSelected();

        Cleaner.CloseRequested += (_, _) => (DataContext as LauncherViewModel)?.CloseCleaner();
    }

    private CommandPaletteViewModel? Palette => (DataContext as LauncherViewModel)?.Palette;

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is LauncherViewModel model && e.AddedItems is [NavItem item, ..])
        {
            model.NavigateCommand.Execute(item);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var palette = Palette;
        if (palette is null)
        {
            return;
        }

        if (!palette.IsOpen)
        {
            if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
            {
                palette.Open();

                // Focus has to wait for the overlay to become visible and lay
                // out; setting it this frame lands on nothing.
                Dispatcher.UIThread.Post(() => PaletteInput.Focus(), DispatcherPriority.Loaded);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                palette.Close();
                e.Handled = true;
                break;

            case Key.Down:
                palette.MoveDown();
                e.Handled = true;
                break;

            case Key.Up:
                palette.MoveUp();
                e.Handled = true;
                break;

            case Key.Enter:
                palette.ChooseSelected();
                e.Handled = true;
                break;
        }
    }
}
