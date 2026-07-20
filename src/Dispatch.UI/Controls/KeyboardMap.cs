using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Dispatch.Core.Controls;

namespace Dispatch.UI.Controls;

/// <summary>How a key on the map is currently being used.</summary>
public enum KeyState
{
    /// <summary>Nothing bound to it.</summary>
    Free,

    /// <summary>Exactly one action bound.</summary>
    Bound,

    /// <summary>More than one action bound on this layer.</summary>
    Conflict,

    /// <summary>Reserved and not reassignable, like the console key.</summary>
    Reserved,
}

/// <summary>
/// A full keyboard drawn to scale, coloured by what each key is doing.
/// </summary>
/// <remarks>
/// Built as a custom control rather than a grid of buttons because a keyboard
/// is not a grid: the rows are offset by different fractions of a key, the
/// modifiers and space bar are various non-integer widths, and the numpad sits
/// in its own block. Approximating that with a uniform grid produces something
/// that reads as a diagram of a keyboard rather than as a keyboard.
///
/// <para>
/// Layout is expressed in key units — one unit is a standard alphanumeric key —
/// and scaled at render time, so the whole map resizes without any of the
/// offsets drifting.
/// </para>
/// </remarks>
public sealed class KeyboardMap : TemplatedControl
{
    /// <summary>Defines the <see cref="Bindings"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<BoundAction>?> BindingsProperty =
        AvaloniaProperty.Register<KeyboardMap, IReadOnlyList<BoundAction>?>(nameof(Bindings));

    /// <summary>Defines the <see cref="Layer"/> property.</summary>
    public static readonly StyledProperty<KeyModifier> LayerProperty =
        AvaloniaProperty.Register<KeyboardMap, KeyModifier>(nameof(Layer));

    /// <summary>Defines the <see cref="SelectedKey"/> property.</summary>
    /// <remarks>
    /// Two-way by default: a click sets it on the control, and the view model
    /// reads it to open the mapping panel, so both directions have to flow.
    /// </remarks>
    public static readonly StyledProperty<KeyToken?> SelectedKeyProperty =
        AvaloniaProperty.Register<KeyboardMap, KeyToken?>(
            nameof(SelectedKey), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Defines the <see cref="FreeKeysOnly"/> property.</summary>
    public static readonly StyledProperty<bool> FreeKeysOnlyProperty =
        AvaloniaProperty.Register<KeyboardMap, bool>(nameof(FreeKeysOnly));

    private Canvas? _surface;

    /// <summary>Every action and what it is bound to.</summary>
    public IReadOnlyList<BoundAction>? Bindings
    {
        get => GetValue(BindingsProperty);
        set => SetValue(BindingsProperty, value);
    }

    /// <summary>
    /// Which modifier layer is shown. Base, Shift, Control or Alt.
    /// </summary>
    /// <remarks>
    /// Essential rather than decorative: <c>Left Shift + X</c> and a bare
    /// <c>X</c> are different bindings, and drawing them on one surface would
    /// show a conflict that does not exist.
    /// </remarks>
    public KeyModifier Layer
    {
        get => GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    /// <summary>The key the user clicked, which filters the list below.</summary>
    public KeyToken? SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    /// <summary>Dims everything already taken, for finding somewhere to put something.</summary>
    public bool FreeKeysOnly
    {
        get => GetValue(FreeKeysOnlyProperty);
        set => SetValue(FreeKeysOnlyProperty, value);
    }

    /// <summary>Raised when a key is clicked.</summary>
    public event EventHandler<KeyToken>? KeySelected;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _surface = e.NameScope.Find<Canvas>("PART_Surface");
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BindingsProperty ||
            change.Property == LayerProperty ||
            change.Property == SelectedKeyProperty ||
            change.Property == FreeKeysOnlyProperty)
        {
            Rebuild();
        }
    }

    /// <summary>Which actions sit on a key, on the current layer.</summary>
    public IReadOnlyList<BoundAction> ActionsOn(KeyToken key)
    {
        if (Bindings is null)
        {
            return [];
        }

        return Bindings
            .Where(bound => bound.Action.Device == InputDevice.Keyboard)
            .Where(bound => !bound.Binding.IsUnbound)
            .Where(bound => bound.Binding.Modifier == Layer)
            .Where(bound => bound.Binding.Key == key)
            .ToList();
    }

    /// <summary>How a key should be drawn on the current layer.</summary>
    public KeyState StateOf(KeyToken key)
    {
        // Fully qualified: Avalonia.Input has a KeyBinding of its own, and both
        // namespaces are in scope in a control.
        if (GameAction.IsReserved(new Dispatch.Core.Controls.KeyBinding(key)))
        {
            return KeyState.Reserved;
        }

        return ActionsOn(key).Count switch
        {
            0 => KeyState.Free,
            1 => KeyState.Bound,
            _ => KeyState.Conflict,
        };
    }

    private void Rebuild()
    {
        if (_surface is null)
        {
            return;
        }

        _surface.Children.Clear();

        const double unit = 44;
        const double gap = 4;

        _surface.Width = (KeyboardLayout.Columns * unit) + gap;
        _surface.Height = (KeyboardLayout.Rows * unit) + gap;

        foreach (var cap in KeyboardLayout.Caps)
        {
            var token = KeyTokens.Parse(cap.Token);
            var state = StateOf(token);
            var actions = ActionsOn(token);

            var key = new Border
            {
                Width = (cap.Width * unit) - gap,
                Height = (cap.Height * unit) - gap,
                CornerRadius = new CornerRadius(6),
                Tag = token,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = cap.Label,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = cap.Label.Length > 3 ? 9.5 : 11.5,
                    TextAlignment = TextAlignment.Center,
                },
            };

            // State is projected onto style classes so the theme owns every
            // brush and this control owns none of them.
            key.Classes.Add("key");
            key.Classes.Add(state switch
            {
                KeyState.Conflict => "conflict",
                KeyState.Bound => "bound",
                KeyState.Reserved => "reserved",
                _ => "free",
            });

            if (SelectedKey == token)
            {
                key.Classes.Add("selected");
            }

            if (FreeKeysOnly && state != KeyState.Free)
            {
                key.Classes.Add("dimmed");
            }

            if (actions.Count > 0)
            {
                ToolTip.SetTip(key, string.Join("\n", actions.Select(a => $"{a.Action.Name} — {a.Action.Plugin}")));
            }
            else if (state == KeyState.Reserved)
            {
                ToolTip.SetTip(key, "Reserved for the RagePluginHook console. Rebinding this removes the only way in when something breaks.");
            }

            key.PointerPressed += (_, _) =>
            {
                SelectedKey = SelectedKey == token ? null : token;
                KeySelected?.Invoke(this, token);
            };

            Canvas.SetLeft(key, (cap.X * unit) + gap);
            Canvas.SetTop(key, (cap.Y * unit) + gap);

            _surface.Children.Add(key);
        }
    }
}
