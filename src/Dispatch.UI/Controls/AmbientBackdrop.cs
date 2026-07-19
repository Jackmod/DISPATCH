using Avalonia;
using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Controls;

/// <summary>
/// The moving field the whole application sits on: drifting light sources, a
/// street grid, a periodic scan sweep and a corner vignette.
/// </summary>
/// <remarks>
/// This exists to make the glass real. Translucent surfaces only read as glass
/// when something is moving behind them — over a flat fill they read as grey
/// plastic. The orbs are large, slow and low-contrast on purpose: the backdrop
/// should be noticed once and then never compete with the content again.
///
/// <para>
/// <see cref="IsAnimated"/> defaults to the <c>MotionEnabled</c> theme
/// resource, which the reduced-motion swap sets to false. Ambient loops have to
/// be switched off rather than merely shortened — an infinite animation with a
/// zero duration is a busy loop, not a still image.
/// </para>
/// </remarks>
public sealed class AmbientBackdrop : TemplatedControl
{
    /// <summary>Defines the <see cref="IsAnimated"/> property.</summary>
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<AmbientBackdrop, bool>(nameof(IsAnimated), true);

    /// <summary>Defines the <see cref="ShowGrid"/> property.</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<AmbientBackdrop, bool>(nameof(ShowGrid), true);

    /// <summary>Defines the <see cref="Intensity"/> property.</summary>
    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<AmbientBackdrop, double>(nameof(Intensity), 1d);

    /// <summary>
    /// Whether the ambient loops run. False leaves the composition in place but
    /// perfectly still.
    /// </summary>
    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>Whether to draw the street grid layer.</summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// Overall strength, 0 to 1. Screens dense with content turn this down so
    /// the backdrop stays subordinate.
    /// </summary>
    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }
}
