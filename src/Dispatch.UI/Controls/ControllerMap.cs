using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dispatch.Core.Controls;

namespace Dispatch.UI.Controls;

/// <summary>
/// A gamepad drawn top-down, coloured by what each button is doing, with the same
/// click-to-select behaviour as the keyboard map.
/// </summary>
/// <remarks>
/// The controller's counterpart to <see cref="KeyboardMap"/>. It draws from
/// <see cref="ControllerLayout"/> and reports a click through the same
/// <see cref="SelectedKey"/> property the mapping panel reads, so one panel serves
/// both devices. Xbox and PlayStation only differ in the face-button glyphs, so a
/// single layout switches labels rather than being drawn twice.
///
/// <para>
/// Built as a <see cref="Canvas"/> rather than a templated control because the
/// body and buttons are freeform vector positions, not a repeatable template, and
/// a canvas of shapes is the honest way to express that.
/// </para>
/// </remarks>
public sealed class ControllerMap : Canvas
{
    /// <summary>Defines the <see cref="Bindings"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<BoundAction>?> BindingsProperty =
        AvaloniaProperty.Register<ControllerMap, IReadOnlyList<BoundAction>?>(nameof(Bindings));

    /// <summary>Defines the <see cref="SelectedKey"/> property.</summary>
    public static readonly StyledProperty<KeyToken?> SelectedKeyProperty =
        AvaloniaProperty.Register<ControllerMap, KeyToken?>(
            nameof(SelectedKey), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Defines the <see cref="PadType"/> property.</summary>
    public static readonly StyledProperty<string> PadTypeProperty =
        AvaloniaProperty.Register<ControllerMap, string>(nameof(PadType), "Xbox");

    /// <summary>Every action and what it is bound to.</summary>
    public IReadOnlyList<BoundAction>? Bindings
    {
        get => GetValue(BindingsProperty);
        set => SetValue(BindingsProperty, value);
    }

    /// <summary>The button the user clicked, shared with the mapping panel.</summary>
    public KeyToken? SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    /// <summary><c>Xbox</c> or <c>PS</c>, which only changes the face-button glyphs.</summary>
    public string PadType
    {
        get => GetValue(PadTypeProperty);
        set => SetValue(PadTypeProperty, value);
    }

    /// <summary>Raised when a button is clicked.</summary>
    public event EventHandler<KeyToken>? KeySelected;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BindingsProperty ||
            change.Property == SelectedKeyProperty ||
            change.Property == PadTypeProperty)
        {
            Rebuild();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private IReadOnlyList<BoundAction> ActionsOn(string token)
    {
        if (Bindings is null)
        {
            return [];
        }

        return Bindings
            .Where(bound => bound.Action.Device == InputDevice.Controller)
            .Where(bound => !bound.Binding.IsUnbound)
            .Where(bound => bound.Binding.Key.Canonical == token)
            .ToList();
    }

    private void Rebuild()
    {
        Children.Clear();
        Width = ControllerLayout.Width;
        Height = ControllerLayout.Height;

        Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse(ControllerLayout.Body),
            Fill = Brush("NavyDeep"),
            Stroke = Brush("Steel"),
            StrokeThickness = 2,
        });

        foreach (var b in ControllerLayout.Buttons)
        {
            var actions = ActionsOn(b.Token);
            var selected = SelectedKey?.Canonical == b.Token;
            var state = actions.Count switch { 0 => 0, 1 => 1, _ => 2 };

            var (fill, stroke) = state switch
            {
                2 => ("NavyRaised", "Red"),
                1 => ("BlueDim", "Blue"),
                _ => ("NavyRaised", "Steel"),
            };

            var glyph = PadType == "PS"
                ? ControllerLayout.PlayStationGlyph(b.Token) ?? b.Label
                : b.Label;

            var width = b.Rect ? b.W : b.R * 2;
            var height = b.Rect ? b.H : b.R * 2;

            var button = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(b.Rect ? 7 : b.R),
                Background = Brush(fill),
                BorderBrush = selected ? Brush("Gold") : Brush(stroke),
                BorderThickness = new Thickness(selected ? 2 : 1.4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = b.Token,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = ResolveFont(),
                    FontSize = b.Rect ? 11 : 12,
                    Foreground = state == 1 ? Brush("BlueBright") : selected ? Brush("GoldBright") : Brush("TextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            if (actions.Count > 0)
            {
                ToolTip.SetTip(button, string.Join("\n", actions.Select(a => $"{a.Action.Name} — {a.Action.Plugin}")));
            }

            var token = new KeyToken(b.Token);
            button.PointerPressed += (_, _) =>
            {
                SelectedKey = SelectedKey?.Canonical == b.Token ? null : token;
                KeySelected?.Invoke(this, token);
            };

            SetLeft(button, b.Cx - (width / 2));
            SetTop(button, b.Cy - (height / 2));
            Children.Add(button);
        }
    }

    private static IBrush Brush(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush
            ? brush
            : Brushes.Gray;

    private static FontFamily ResolveFont() =>
        Application.Current?.TryGetResource("MonoFont", null, out var value) == true && value is FontFamily font
            ? font
            : FontFamily.Default;
}
