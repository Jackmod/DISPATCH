using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dispatch.UI.Gallery;

/// <summary>
/// Development tool: the whole design system on one screen, in every state.
/// Not reachable from the shipped navigation.
/// </summary>
public partial class GalleryView : UserControl
{
    private bool _reducedMotion;

    /// <summary>Constructs the gallery.</summary>
    public GalleryView() => InitializeComponent();

    /// <summary>
    /// Flips reduced motion so the setting can be judged by eye rather than
    /// only by test. Lives in code-behind because the gallery is a development
    /// tool with no view model behind it; the shipped Settings screen drives
    /// the same call through one.
    /// </summary>
    private void OnToggleReducedMotion(object? sender, RoutedEventArgs e)
    {
        _reducedMotion = !_reducedMotion;

        (Application.Current as App)?.SetReducedMotion(_reducedMotion);

        ReducedMotionToggle.Content = _reducedMotion
            ? "Reduced motion: ON"
            : "Reduced motion: OFF";
    }
}
