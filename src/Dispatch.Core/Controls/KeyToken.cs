using System.Diagnostics.CodeAnalysis;

namespace Dispatch.Core.Controls;

/// <summary>
/// The token format a particular mod writes to its config file.
/// </summary>
/// <remarks>
/// There is no agreement between these mods. LSPDFR writes WinForms
/// <c>Keys</c> names with a suffix (<c>LShiftKey</c>), Stop The Ped writes the
/// XNA-style enum (<c>D9</c> for the number row), several write a bare letter,
/// and controller bindings are their own vocabulary again. Translating on read
/// and write is the entire reason no raw token ever reaches the screen.
/// </remarks>
public enum KeyDialect
{
    /// <summary>WinForms <c>Keys</c> names, as LSPDFR writes them: <c>LShiftKey</c>, <c>D9</c>, <c>NumPad7</c>.</summary>
    WinForms,

    /// <summary>Bare characters and simple names, as several plugins write them: <c>X</c>, <c>F9</c>.</summary>
    Bare,

    /// <summary>XInput button names: <c>DPadRight</c>, <c>RightThumb</c>.</summary>
    Controller,
}

/// <summary>
/// One physical input — a key or a controller button — held in a canonical
/// form so that mods disagreeing about spelling cannot disagree about meaning.
/// </summary>
public readonly record struct KeyToken(string Canonical)
{
    /// <summary>The unbound token. Mods write this as <c>None</c>.</summary>
    public static readonly KeyToken None = new("None");

    /// <summary>True when nothing is bound.</summary>
    public bool IsUnbound => string.IsNullOrEmpty(Canonical) || Canonical == "None";

    /// <summary>True when this is a numpad key, which needs Num Lock to register.</summary>
    public bool IsNumpad => Canonical.StartsWith("NumPad", StringComparison.Ordinal);

    /// <summary>True when this is a controller input rather than a key.</summary>
    public bool IsControllerInput => ControllerInputs.Contains(Canonical);

    /// <inheritdoc />
    public override string ToString() => Canonical;

    /// <remarks>
    /// Face buttons carry a <c>Pad</c> prefix in canonical form. A, B, X and Y
    /// are also perfectly ordinary keyboard letters, and without the prefix a
    /// keyboard bind on X is indistinguishable from the gamepad's X button —
    /// which made every keyboard letter bind report itself as a controller
    /// input.
    /// </remarks>
    private static readonly HashSet<string> ControllerInputs = new(StringComparer.Ordinal)
    {
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
        "PadA", "PadB", "PadX", "PadY",
        "LeftShoulder", "RightShoulder",
        "LeftTrigger", "RightTrigger",
        "LeftThumb", "RightThumb",
        "PadStart", "PadBack",
    };
}

/// <summary>
/// Translates between the token a config file holds and the words a person
/// reads.
/// </summary>
/// <remarks>
/// Deliberately table-driven rather than algorithmic. The mappings are not
/// derivable — <c>D9</c> meaning "9 on the number row" and <c>OemQuestion</c>
/// meaning the slash key are historical accidents, and a clever parser would
/// be wrong in ways nobody notices until a bind silently stops working.
/// </remarks>
public static class KeyTokens
{
    /// <summary>
    /// Canonical token to the words shown on screen.
    /// </summary>
    /// <remarks>
    /// Number-row digits carry the qualifier because a bare "9" beside
    /// "Numpad 9" in a list is genuinely ambiguous, and confusing the two is
    /// the single most common keybinding mistake in this ecosystem.
    /// </remarks>
    private static readonly Dictionary<string, string> Display = new(StringComparer.Ordinal)
    {
        ["None"] = "Unbound",

        ["LShiftKey"] = "Left Shift",
        ["RShiftKey"] = "Right Shift",
        ["LControlKey"] = "Left Control",
        ["RControlKey"] = "Right Control",
        ["LMenu"] = "Left Alt",
        ["RMenu"] = "Right Alt",

        ["Space"] = "Space",
        ["Return"] = "Enter",
        ["Escape"] = "Esc",
        ["Back"] = "Backspace",
        ["Tab"] = "Tab",
        ["Delete"] = "Delete",
        ["Insert"] = "Insert",
        ["Home"] = "Home",
        ["End"] = "End",
        ["PageUp"] = "Page Up",
        ["PageDown"] = "Page Down",

        ["Up"] = "Arrow Up",
        ["Down"] = "Arrow Down",
        ["Left"] = "Arrow Left",
        ["Right"] = "Arrow Right",

        ["Oemtilde"] = "` (backtick)",
        ["OemMinus"] = "- (minus)",
        ["Oemplus"] = "= (equals)",
        ["OemQuestion"] = "/ (slash)",
        ["OemPeriod"] = ". (period)",
        ["Oemcomma"] = ", (comma)",
        ["OemSemicolon"] = "; (semicolon)",
        ["OemQuotes"] = "' (quote)",
        ["OemOpenBrackets"] = "[ (bracket)",
        ["OemCloseBrackets"] = "] (bracket)",
        ["OemPipe"] = "\\ (backslash)",

        ["Multiply"] = "Numpad *",
        ["Add"] = "Numpad +",
        ["Subtract"] = "Numpad -",
        ["Divide"] = "Numpad /",
        ["Decimal"] = "Numpad .",

        ["DPadUp"] = "D-Pad Up",
        ["DPadDown"] = "D-Pad Down",
        ["DPadLeft"] = "D-Pad Left",
        ["DPadRight"] = "D-Pad Right",
        ["PadA"] = "A Button",
        ["PadB"] = "B Button",
        ["PadX"] = "X Button",
        ["PadY"] = "Y Button",
        ["LeftShoulder"] = "Left Bumper",
        ["RightShoulder"] = "Right Bumper",
        ["LeftTrigger"] = "Left Trigger",
        ["RightTrigger"] = "Right Trigger",
        ["LeftThumb"] = "Left Stick (click)",
        ["RightThumb"] = "Right Stick (click)",
        ["PadStart"] = "Start",
        ["PadBack"] = "Back",
    };

    /// <summary>
    /// Controller inputs a mod writes without a prefix, mapped to the
    /// prefixed canonical form.
    /// </summary>
    private static readonly Dictionary<string, string> ControllerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "PadA",
        ["B"] = "PadB",
        ["X"] = "PadX",
        ["Y"] = "PadY",
        ["Start"] = "PadStart",
        ["Back"] = "PadBack",
    };

    /// <summary>Parses a token written by a mod into canonical form.</summary>
    /// <param name="raw">The value as it appears in the config file.</param>
    /// <param name="dialect">Which format that mod writes.</param>
    public static KeyToken Parse(string? raw, KeyDialect dialect = KeyDialect.WinForms)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return KeyToken.None;
        }

        var value = raw.Trim();

        if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "NONE", StringComparison.Ordinal) ||
            value == "0")
        {
            return KeyToken.None;
        }

        // Controller inputs share spelling across dialects.
        if (dialect == KeyDialect.Controller)
        {
            return new KeyToken(NormaliseControllerInput(value));
        }

        // A single letter or digit, however the mod chose to write it.
        if (value.Length == 1)
        {
            var ch = char.ToUpperInvariant(value[0]);
            return char.IsAsciiDigit(ch) ? new KeyToken($"D{ch}") : new KeyToken(ch.ToString());
        }

        // D0-D9 is the number row; NumPad0-9 is the pad. Both arrive as-is.
        if (value.Length == 2 && value[0] is 'D' or 'd' && char.IsAsciiDigit(value[1]))
        {
            return new KeyToken($"D{value[1]}");
        }

        if (value.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
        {
            return new KeyToken("NumPad" + value[6..]);
        }

        if (value.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            value.Length <= 3 &&
            int.TryParse(value.AsSpan(1), out var function) &&
            function is >= 1 and <= 24)
        {
            return new KeyToken($"F{function}");
        }

        // Anything else keeps the mod's own spelling, matched case-insensitively
        // against the display table so casing differences do not create two
        // tokens meaning the same key.
        var known = Display.Keys.FirstOrDefault(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase));
        return new KeyToken(known ?? value);
    }

    /// <summary>Formats a canonical token back into the dialect a mod expects.</summary>
    public static string Format(KeyToken token, KeyDialect dialect = KeyDialect.WinForms)
    {
        if (token.IsUnbound)
        {
            return "None";
        }

        return dialect switch
        {
            // Bare drops the D prefix from number-row digits; everything else
            // is spelled the same way.
            KeyDialect.Bare when IsNumberRow(token, out var digit) => digit.ToString(),

            // The Pad prefix is ours, not the mod's. Config files expect the
            // bare face-button letter.
            KeyDialect.Controller when token.Canonical.StartsWith("Pad", StringComparison.Ordinal)
                => token.Canonical[3..],

            _ => token.Canonical,
        };
    }

    /// <summary>The words shown on screen for a token. Never a raw token.</summary>
    public static string ToDisplay(KeyToken token)
    {
        if (token.IsUnbound)
        {
            return "Unbound";
        }

        if (Display.TryGetValue(token.Canonical, out var known))
        {
            return known;
        }

        if (IsNumberRow(token, out var digit))
        {
            return $"{digit} (number row)";
        }

        if (token.Canonical.StartsWith("NumPad", StringComparison.Ordinal))
        {
            return $"Numpad {token.Canonical[6..]}";
        }

        // Single letters and function keys read correctly as themselves.
        return token.Canonical;
    }

    /// <summary>True when the token is a number-row digit, yielding the digit.</summary>
    public static bool IsNumberRow(KeyToken token, out char digit)
    {
        if (token.Canonical.Length == 2 &&
            token.Canonical[0] == 'D' &&
            char.IsAsciiDigit(token.Canonical[1]))
        {
            digit = token.Canonical[1];
            return true;
        }

        digit = default;
        return false;
    }

    /// <summary>Every canonical token the display table knows, for the map legend.</summary>
    public static IReadOnlyCollection<string> KnownTokens => Display.Keys;

    private static string NormaliseControllerInput(string value)
    {
        // Face buttons gain their Pad prefix here, so a gamepad X and a
        // keyboard X never collapse to the same token.
        if (ControllerAliases.TryGetValue(value, out var prefixed))
        {
            return prefixed;
        }

        return Display.Keys.FirstOrDefault(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase))
            ?? value;
    }
}

/// <summary>Modifier keys that can qualify a binding.</summary>
[Flags]
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "A byte is ample for four flags and keeps the serialized profile small.")]
public enum KeyModifier : byte
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift.</summary>
    Shift = 1,

    /// <summary>Control.</summary>
    Control = 2,

    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>
/// A complete binding: a key, plus any modifier held with it.
/// </summary>
/// <remarks>
/// Modifiers are part of the identity rather than decoration. <c>Left Shift +
/// X</c> and a bare <c>X</c> are different bindings and must not be reported
/// as conflicting, which is the entire reason the keyboard map has layers.
/// </remarks>
public readonly record struct KeyBinding(KeyToken Key, KeyModifier Modifier = KeyModifier.None)
{
    /// <summary>An unbound binding.</summary>
    public static readonly KeyBinding Unbound = new(KeyToken.None);

    /// <summary>True when nothing is bound.</summary>
    public bool IsUnbound => Key.IsUnbound;

    /// <summary>The whole binding in plain words, for example <c>Left Shift + X</c>.</summary>
    public string Display
    {
        get
        {
            if (IsUnbound)
            {
                return "Unbound";
            }

            var key = KeyTokens.ToDisplay(Key);

            var parts = new List<string>(4);
            if (Modifier.HasFlag(KeyModifier.Control)) { parts.Add("Left Control"); }
            if (Modifier.HasFlag(KeyModifier.Shift)) { parts.Add("Left Shift"); }
            if (Modifier.HasFlag(KeyModifier.Alt)) { parts.Add("Left Alt"); }

            parts.Add(key);
            return string.Join(" + ", parts);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Display;
}
