using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dispatch.Core.Controls;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The controls screen. Code-behind handles key capture, which has to be done
/// at the view because it is raw input rather than a bindable value.
/// </summary>
public partial class ControlsView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public ControlsView()
    {
        InitializeComponent();

        // Tunnelling, so capture sees the key before focus navigation or a
        // text box consumes it.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Read the real bindings off disk and fold in discovered binds on first show.
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ControlsViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ControlsViewModel model)
        {
            return;
        }

        // Ctrl+F focuses search, as the spec asks, and only when not capturing.
        if (model.Capturing is null)
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                model.CancelCaptureCommand.Execute(null);
                e.Handled = true;
                return;

            case Key.Delete or Key.Back:
                model.UnbindCommand.Execute(model.Capturing);
                e.Handled = true;
                return;

            // A modifier alone is not a binding — the user is still mid-chord.
            case Key.LeftShift or Key.RightShift
                or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt:
                return;
        }

        model.ApplyCapture(ToToken(e.Key), ToModifier(e.KeyModifiers));
        e.Handled = true;
    }

    /// <summary>
    /// Maps an Avalonia key to a canonical token.
    /// </summary>
    /// <remarks>
    /// Avalonia's names and the WinForms names the mods write differ in
    /// exactly the places that matter — digits, the numpad and the modifiers —
    /// so this cannot be a ToString.
    /// </remarks>
    private static KeyToken ToToken(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => new KeyToken($"D{(int)key - (int)Key.D0}"),
        >= Key.NumPad0 and <= Key.NumPad9 => new KeyToken($"NumPad{(int)key - (int)Key.NumPad0}"),
        >= Key.A and <= Key.Z => new KeyToken(key.ToString()),
        >= Key.F1 and <= Key.F24 => new KeyToken(key.ToString()),
        Key.Space => new KeyToken("Space"),
        Key.Enter => new KeyToken("Return"),
        Key.Tab => new KeyToken("Tab"),
        Key.Insert => new KeyToken("Insert"),
        Key.Home => new KeyToken("Home"),
        Key.End => new KeyToken("End"),
        Key.PageUp => new KeyToken("PageUp"),
        Key.PageDown => new KeyToken("PageDown"),
        Key.Up => new KeyToken("Up"),
        Key.Down => new KeyToken("Down"),
        Key.Left => new KeyToken("Left"),
        Key.Right => new KeyToken("Right"),
        Key.OemTilde => new KeyToken("Oemtilde"),
        Key.OemMinus => new KeyToken("OemMinus"),
        Key.OemPlus => new KeyToken("Oemplus"),
        Key.OemQuestion => new KeyToken("OemQuestion"),
        Key.OemPeriod => new KeyToken("OemPeriod"),
        Key.OemComma => new KeyToken("Oemcomma"),
        Key.OemSemicolon => new KeyToken("OemSemicolon"),
        Key.OemQuotes => new KeyToken("OemQuotes"),
        Key.OemOpenBrackets => new KeyToken("OemOpenBrackets"),
        Key.OemCloseBrackets => new KeyToken("OemCloseBrackets"),
        Key.OemPipe => new KeyToken("OemPipe"),
        Key.Multiply => new KeyToken("Multiply"),
        Key.Add => new KeyToken("Add"),
        Key.Subtract => new KeyToken("Subtract"),
        Key.Divide => new KeyToken("Divide"),
        Key.Decimal => new KeyToken("Decimal"),
        _ => new KeyToken(key.ToString()),
    };

    private static KeyModifier ToModifier(KeyModifiers modifiers)
    {
        var result = KeyModifier.None;

        if (modifiers.HasFlag(KeyModifiers.Shift)) { result |= KeyModifier.Shift; }
        if (modifiers.HasFlag(KeyModifiers.Control)) { result |= KeyModifier.Control; }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { result |= KeyModifier.Alt; }

        return result;
    }
}
