namespace Dispatch.UI.Controls;

/// <summary>One key cap: where it sits, how big it is, and what it says.</summary>
/// <param name="Token">Canonical token this cap represents.</param>
/// <param name="Label">What is printed on it.</param>
/// <param name="X">Left edge, in key units from the left of the board.</param>
/// <param name="Y">Top edge, in key units from the top.</param>
/// <param name="Width">Width in key units. 1 is a standard alphanumeric key.</param>
/// <param name="Height">Height in key units.</param>
public sealed record KeyCap(
    string Token,
    string Label,
    double X,
    double Y,
    double Width = 1,
    double Height = 1);

/// <summary>
/// A full ANSI keyboard with numpad, expressed in key units.
/// </summary>
/// <remarks>
/// Units rather than pixels so the whole board scales from one multiplier
/// without the row offsets drifting. The offsets are the point: real rows step
/// by a quarter, a half and three quarters of a key, and a board drawn on
/// whole units reads as a diagram rather than as a keyboard.
///
/// <para>
/// Tokens are the canonical form, so this table is also the definitive list of
/// what the map can display. A bind on a key not listed here still works — it
/// simply has nowhere to light up, which is why the action list never depends
/// on the map.
/// </para>
/// </remarks>
public static class KeyboardLayout
{
    /// <summary>
    /// Width of the board in key units, numpad included.
    /// </summary>
    /// <remarks>
    /// The numpad's rightmost column sits at x=22 and is one unit wide, so the
    /// board needs 23 plus margin. Under-declaring this clipped the plus and
    /// enter keys off the right edge.
    /// </remarks>
    public const double Columns = 23.5;

    /// <summary>Height of the board in key units.</summary>
    public const double Rows = 6.5;

    /// <summary>Every cap, in reading order.</summary>
    public static readonly IReadOnlyList<KeyCap> Caps = Build();

    private static List<KeyCap> Build()
    {
        var caps = new List<KeyCap>();

        // ===== Function row ==============================================
        caps.Add(new KeyCap("Escape", "Esc", 0, 0));

        var fx = 2.0;
        for (var i = 1; i <= 12; i++)
        {
            // Function keys cluster in groups of four, as on a real board.
            if (i is 5 or 9)
            {
                fx += 0.5;
            }

            caps.Add(new KeyCap($"F{i}", $"F{i}", fx, 0));
            fx += 1;
        }

        // ===== Number row ================================================
        caps.Add(new KeyCap("Oemtilde", "`", 0, 1.25));
        for (var i = 1; i <= 9; i++)
        {
            caps.Add(new KeyCap($"D{i}", i.ToString(), i, 1.25));
        }

        caps.Add(new KeyCap("D0", "0", 10, 1.25));
        caps.Add(new KeyCap("OemMinus", "-", 11, 1.25));
        caps.Add(new KeyCap("Oemplus", "=", 12, 1.25));
        caps.Add(new KeyCap("Back", "Backspace", 13, 1.25, 2));

        // ===== QWERTY row ================================================
        caps.Add(new KeyCap("Tab", "Tab", 0, 2.25, 1.5));
        var q = 1.5;
        foreach (var letter in "QWERTYUIOP")
        {
            caps.Add(new KeyCap(letter.ToString(), letter.ToString(), q, 2.25));
            q += 1;
        }

        caps.Add(new KeyCap("OemOpenBrackets", "[", q, 2.25));
        caps.Add(new KeyCap("OemCloseBrackets", "]", q + 1, 2.25));
        caps.Add(new KeyCap("OemPipe", "\\", q + 2, 2.25, 1.5));

        // ===== Home row ==================================================
        caps.Add(new KeyCap("CapsLock", "Caps", 0, 3.25, 1.75));
        var a = 1.75;
        foreach (var letter in "ASDFGHJKL")
        {
            caps.Add(new KeyCap(letter.ToString(), letter.ToString(), a, 3.25));
            a += 1;
        }

        caps.Add(new KeyCap("OemSemicolon", ";", a, 3.25));
        caps.Add(new KeyCap("OemQuotes", "'", a + 1, 3.25));
        caps.Add(new KeyCap("Return", "Enter", a + 2, 3.25, 2.25));

        // ===== Bottom letter row =========================================
        caps.Add(new KeyCap("LShiftKey", "Shift", 0, 4.25, 2.25));
        var z = 2.25;
        foreach (var letter in "ZXCVBNM")
        {
            caps.Add(new KeyCap(letter.ToString(), letter.ToString(), z, 4.25));
            z += 1;
        }

        caps.Add(new KeyCap("Oemcomma", ",", z, 4.25));
        caps.Add(new KeyCap("OemPeriod", ".", z + 1, 4.25));
        caps.Add(new KeyCap("OemQuestion", "/", z + 2, 4.25));
        caps.Add(new KeyCap("RShiftKey", "Shift", z + 3, 4.25, 2.75));

        // ===== Modifier row ==============================================
        caps.Add(new KeyCap("LControlKey", "Ctrl", 0, 5.25, 1.5));
        caps.Add(new KeyCap("LWin", "Win", 1.5, 5.25, 1.25));
        caps.Add(new KeyCap("LMenu", "Alt", 2.75, 5.25, 1.25));
        caps.Add(new KeyCap("Space", "Space", 4, 5.25, 6.25));
        caps.Add(new KeyCap("RMenu", "Alt", 10.25, 5.25, 1.25));
        caps.Add(new KeyCap("RWin", "Win", 11.5, 5.25, 1.25));
        caps.Add(new KeyCap("RControlKey", "Ctrl", 12.75, 5.25, 1.5));

        // ===== Navigation cluster ========================================
        caps.Add(new KeyCap("Insert", "Ins", 15.5, 1.25));
        caps.Add(new KeyCap("Home", "Home", 16.5, 1.25));
        caps.Add(new KeyCap("PageUp", "PgUp", 17.5, 1.25));
        caps.Add(new KeyCap("Delete", "Del", 15.5, 2.25));
        caps.Add(new KeyCap("End", "End", 16.5, 2.25));
        caps.Add(new KeyCap("PageDown", "PgDn", 17.5, 2.25));

        caps.Add(new KeyCap("Up", "↑", 16.5, 4.25));
        caps.Add(new KeyCap("Left", "←", 15.5, 5.25));
        caps.Add(new KeyCap("Down", "↓", 16.5, 5.25));
        caps.Add(new KeyCap("Right", "→", 17.5, 5.25));

        // ===== Numpad ====================================================
        // Called out because numpad binds only register with Num Lock on,
        // which is the single most common "my bind stopped working" cause.
        caps.Add(new KeyCap("NumLock", "Num", 19, 1.25));
        caps.Add(new KeyCap("Divide", "/", 20, 1.25));
        caps.Add(new KeyCap("Multiply", "*", 21, 1.25));
        caps.Add(new KeyCap("Subtract", "-", 22, 1.25));

        caps.Add(new KeyCap("NumPad7", "7", 19, 2.25));
        caps.Add(new KeyCap("NumPad8", "8", 20, 2.25));
        caps.Add(new KeyCap("NumPad9", "9", 21, 2.25));
        caps.Add(new KeyCap("Add", "+", 22, 2.25, 1, 2));

        caps.Add(new KeyCap("NumPad4", "4", 19, 3.25));
        caps.Add(new KeyCap("NumPad5", "5", 20, 3.25));
        caps.Add(new KeyCap("NumPad6", "6", 21, 3.25));

        caps.Add(new KeyCap("NumPad1", "1", 19, 4.25));
        caps.Add(new KeyCap("NumPad2", "2", 20, 4.25));
        caps.Add(new KeyCap("NumPad3", "3", 21, 4.25));
        caps.Add(new KeyCap("NumPadEnter", "Ent", 22, 4.25, 1, 2));

        caps.Add(new KeyCap("NumPad0", "0", 19, 5.25, 2));
        caps.Add(new KeyCap("Decimal", ".", 21, 5.25));

        return caps;
    }
}
