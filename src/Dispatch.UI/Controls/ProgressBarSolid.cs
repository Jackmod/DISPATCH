using Avalonia;
using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Controls;

/// <summary>
/// A solid determinate progress bar with the percentage alongside it.
/// </summary>
/// <remarks>
/// Used for the install run. The lightbar rail says which of seven phases is
/// running, which is the more useful fact, but it cannot say how far through
/// the current phase the run is — and during a fourteen-step download phase
/// that leaves a static screen for minutes at a time. A moving bar with a
/// number is what stops a slow run reading as a hang.
///
/// <para>
/// The fill animates toward its new value rather than jumping. Progress
/// arrives in discrete steps and an unanimated bar visibly ratchets, which
/// reads as stuttering rather than as steady work.
/// </para>
/// </remarks>
public sealed class ProgressBarSolid : TemplatedControl
{
    /// <summary>Defines the <see cref="Fraction"/> property.</summary>
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ProgressBarSolid, double>(nameof(Fraction));

    /// <summary>Defines the <see cref="ShowPercent"/> property.</summary>
    public static readonly StyledProperty<bool> ShowPercentProperty =
        AvaloniaProperty.Register<ProgressBarSolid, bool>(nameof(ShowPercent), true);

    /// <summary>Defines the <see cref="BarHeight"/> property.</summary>
    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<ProgressBarSolid, double>(nameof(BarHeight), 10d);

    /// <summary>Defines the <see cref="TrackWidth"/> property.</summary>
    public static readonly DirectProperty<ProgressBarSolid, double> TrackWidthProperty =
        AvaloniaProperty.RegisterDirect<ProgressBarSolid, double>(
            nameof(TrackWidth), o => o.TrackWidth);

    /// <summary>Defines the <see cref="PercentText"/> property.</summary>
    public static readonly DirectProperty<ProgressBarSolid, string> PercentTextProperty =
        AvaloniaProperty.RegisterDirect<ProgressBarSolid, string>(
            nameof(PercentText), o => o.PercentText);

    private double _trackWidth;
    private string _percentText = "0%";
    private double _availableWidth;

    /// <summary>Progress through the run, 0 to 1.</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>Whether to show the percentage beside the bar.</summary>
    public bool ShowPercent
    {
        get => GetValue(ShowPercentProperty);
        set => SetValue(ShowPercentProperty, value);
    }

    /// <summary>Thickness of the bar.</summary>
    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    /// <summary>
    /// Width of the filled portion in pixels.
    /// </summary>
    /// <remarks>
    /// Computed rather than expressed as a percentage width, because Avalonia
    /// has no proportional width primitive and a ScaleTransform on a rounded
    /// bar would squash the corner radius along with it.
    /// </remarks>
    public double TrackWidth
    {
        get => _trackWidth;
        private set => SetAndRaise(TrackWidthProperty, ref _trackWidth, value);
    }

    /// <summary>The percentage, formatted for display.</summary>
    public string PercentText
    {
        get => _percentText;
        private set => SetAndRaise(PercentTextProperty, ref _percentText, value);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        // The percentage label takes fixed space beside the track.
        _availableWidth = Math.Max(0, arranged.Width - (ShowPercent ? 64 : 0));
        UpdateTrack();

        return arranged;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FractionProperty || change.Property == ShowPercentProperty)
        {
            UpdateTrack();
        }
    }

    private void UpdateTrack()
    {
        var clamped = Math.Clamp(Fraction, 0, 1);

        TrackWidth = _availableWidth * clamped;
        PercentText = $"{(int)Math.Round(clamped * 100)}%";
    }
}
