using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Dispatch.UI.Controls;

/// <summary>
/// The signature motif doing real work: a row of lozenges that fills left to
/// right, one segment per phase.
/// </summary>
/// <remarks>
/// Used as the wizard's top rail and as the install progress indicator. A
/// segment count rather than a percentage is deliberate — an install has seven
/// named phases, and "Placing files" is information a percentage does not
/// carry.
///
/// <para>
/// The segments are built in code rather than declared in the template because
/// the count is data-driven. Everything about how they look still comes from
/// the theme.
/// </para>
/// </remarks>
public sealed class LightbarProgress : TemplatedControl
{
    private Panel? _host;

    /// <summary>Defines the <see cref="SegmentCount"/> property.</summary>
    public static readonly StyledProperty<int> SegmentCountProperty =
        AvaloniaProperty.Register<LightbarProgress, int>(nameof(SegmentCount), 7);

    /// <summary>Defines the <see cref="CompletedSegments"/> property.</summary>
    public static readonly StyledProperty<int> CompletedSegmentsProperty =
        AvaloniaProperty.Register<LightbarProgress, int>(nameof(CompletedSegments));

    /// <summary>Defines the <see cref="SegmentHeight"/> property.</summary>
    public static readonly StyledProperty<double> SegmentHeightProperty =
        AvaloniaProperty.Register<LightbarProgress, double>(nameof(SegmentHeight), 4d);

    /// <summary>Defines the <see cref="SegmentGap"/> property.</summary>
    public static readonly StyledProperty<double> SegmentGapProperty =
        AvaloniaProperty.Register<LightbarProgress, double>(nameof(SegmentGap), 4d);

    /// <summary>Defines the <see cref="IsAnimated"/> property.</summary>
    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<LightbarProgress, bool>(nameof(IsAnimated), true);

    /// <summary>How many phases the run has.</summary>
    public int SegmentCount
    {
        get => GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    /// <summary>How many phases are done. The next one reads as in progress.</summary>
    public int CompletedSegments
    {
        get => GetValue(CompletedSegmentsProperty);
        set => SetValue(CompletedSegmentsProperty, value);
    }

    /// <summary>Lozenge height. 3px as a wizard rail, 8px as an install indicator.</summary>
    public double SegmentHeight
    {
        get => GetValue(SegmentHeightProperty);
        set => SetValue(SegmentHeightProperty, value);
    }

    /// <summary>Gap between lozenges.</summary>
    public double SegmentGap
    {
        get => GetValue(SegmentGapProperty);
        set => SetValue(SegmentGapProperty, value);
    }

    /// <summary>Whether the in-progress segment pulses.</summary>
    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _host = e.NameScope.Find<Panel>("PART_Segments");
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SegmentCountProperty ||
            change.Property == SegmentHeightProperty ||
            change.Property == SegmentGapProperty)
        {
            Rebuild();
        }
        else if (change.Property == CompletedSegmentsProperty || change.Property == IsAnimatedProperty)
        {
            UpdateSegmentStates();
        }
    }

    private void Rebuild()
    {
        if (_host is null)
        {
            return;
        }

        _host.Children.Clear();

        var count = Math.Max(1, SegmentCount);
        var grid = new Grid { ColumnDefinitions = [] };

        for (var i = 0; i < count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            var segment = new Rectangle
            {
                RadiusX = SegmentHeight / 2,
                RadiusY = SegmentHeight / 2,
                Height = SegmentHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(i == 0 ? 0 : SegmentGap / 2, 0, i == count - 1 ? 0 : SegmentGap / 2, 0),
            };

            Grid.SetColumn(segment, i);
            grid.Children.Add(segment);
        }

        _host.Children.Add(grid);
        UpdateSegmentStates();
    }

    // Segment state is expressed as style classes so the theme owns every
    // brush and the control owns none of them.
    private void UpdateSegmentStates()
    {
        if (_host?.Children.FirstOrDefault() is not Grid grid)
        {
            return;
        }

        for (var i = 0; i < grid.Children.Count; i++)
        {
            if (grid.Children[i] is not Rectangle segment)
            {
                continue;
            }

            var done = i < CompletedSegments;
            var current = i == CompletedSegments && CompletedSegments < SegmentCount;

            segment.Classes.Set("done", done);
            segment.Classes.Set("current", current && IsAnimated);
            segment.Classes.Set("currentStill", current && !IsAnimated);
            segment.Classes.Set("pending", !done && !current);
        }
    }
}
