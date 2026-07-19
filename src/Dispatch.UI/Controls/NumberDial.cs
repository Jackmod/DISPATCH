using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Dispatch.UI.Controls;

/// <summary>
/// A bounded integer picker: the value large and centred, with a step control
/// either side.
/// </summary>
/// <remarks>
/// Built rather than themed over NumericUpDown. That control is a TextBox plus
/// a spinner, and its value is a nullable decimal with free text entry — which
/// means validating input, handling nulls, and fighting a template built around
/// a text field. The callsign division and beat are bounded small integers that
/// are never typed, so a dial is both simpler and closer to what the screen is
/// actually asking for.
///
/// <para>
/// Wraps at both ends: a callsign dial that stops dead at 10 makes the user
/// reverse direction to reach 1, which is a worse interaction than wrapping.
/// </para>
/// </remarks>
public sealed class NumberDial : TemplatedControl
{
    /// <summary>Defines the <see cref="Value"/> property.</summary>
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<NumberDial, int>(
            nameof(Value), 1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Defines the <see cref="Minimum"/> property.</summary>
    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<NumberDial, int>(nameof(Minimum), 1);

    /// <summary>Defines the <see cref="Maximum"/> property.</summary>
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<NumberDial, int>(nameof(Maximum), 10);

    /// <summary>The current value, always within range.</summary>
    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Lowest allowed value, inclusive.</summary>
    public int Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Highest allowed value, inclusive.</summary>
    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Button>("PART_Down") is { } down)
        {
            down.Click += (_, _) => Step(-1);
        }

        if (e.NameScope.Find<Button>("PART_Up") is { } up)
        {
            up.Click += (_, _) => Step(1);
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Up or Key.Right:
                Step(1);
                e.Handled = true;
                break;

            case Key.Down or Key.Left:
                Step(-1);
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty ||
            change.Property == MinimumProperty ||
            change.Property == MaximumProperty)
        {
            Clamp();
        }
    }

    private void Step(int delta)
    {
        if (Maximum < Minimum)
        {
            return;
        }

        var span = Maximum - Minimum + 1;
        var offset = Value - Minimum + delta;

        // Modulo twice so a negative step wraps to the top rather than
        // producing a negative index.
        Value = Minimum + (((offset % span) + span) % span);
    }

    private void Clamp()
    {
        if (Maximum < Minimum)
        {
            return;
        }

        var clamped = Math.Clamp(Value, Minimum, Maximum);
        if (clamped != Value)
        {
            Value = clamped;
        }
    }
}
