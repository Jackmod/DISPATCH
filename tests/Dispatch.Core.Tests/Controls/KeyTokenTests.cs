using Dispatch.Core.Controls;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Controls;

/// <summary>
/// Translation between what a config file holds and what a person reads.
///
/// The rule the whole controls screen rests on is that no raw token ever
/// reaches the screen, and that a value read from one mod can be written back
/// in that same mod's dialect unchanged. A round trip that alters a token
/// silently rebinds a key.
/// </summary>
public sealed class KeyTokenTests
{
    // ===== The mappings the spec names explicitly =========================

    [Theory]
    [InlineData("LShiftKey", "Left Shift")]
    [InlineData("D9", "9 (number row)")]
    [InlineData("NumPad7", "Numpad 7")]
    [InlineData("DPadRight", "D-Pad Right")]
    [InlineData("RightThumb", "Right Stick (click)")]
    [InlineData("None", "Unbound")]
    public void Tokens_read_as_plain_english(string token, string expected) =>
        KeyTokens.ToDisplay(new KeyToken(token)).Should().Be(expected);

    [Fact]
    public void A_number_row_digit_is_distinguished_from_the_numpad()
    {
        // Confusing these two is the most common keybinding mistake in this
        // ecosystem, so the display text disambiguates rather than showing "9"
        // twice in one list.
        KeyTokens.ToDisplay(new KeyToken("D9")).Should().Be("9 (number row)");
        KeyTokens.ToDisplay(new KeyToken("NumPad9")).Should().Be("Numpad 9");
    }

    // ===== Parsing across dialects ========================================

    [Theory]
    [InlineData("X", "X")]
    [InlineData("x", "X")]
    [InlineData("9", "D9")]
    [InlineData("D9", "D9")]
    [InlineData("d9", "D9")]
    [InlineData("F9", "F9")]
    [InlineData("f9", "F9")]
    [InlineData("NumPad7", "NumPad7")]
    [InlineData("numpad7", "NumPad7")]
    public void Dialect_spellings_converge_on_one_canonical_token(string raw, string canonical) =>
        KeyTokens.Parse(raw).Canonical.Should().Be(canonical);

    [Theory]
    [InlineData("None")]
    [InlineData("NONE")]
    [InlineData("none")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Every_spelling_of_unbound_parses_to_unbound(string? raw) =>
        KeyTokens.Parse(raw).IsUnbound.Should().BeTrue();

    [Fact]
    public void Case_differences_do_not_create_two_tokens_for_one_key()
    {
        // Otherwise the same key bound by two mods reads as two keys, and the
        // conflict detector never fires.
        KeyTokens.Parse("lshiftkey").Should().Be(KeyTokens.Parse("LShiftKey"));
    }

    // ===== Round trips ====================================================

    [Theory]
    [InlineData("LShiftKey", KeyDialect.WinForms, "LShiftKey")]
    [InlineData("D9", KeyDialect.WinForms, "D9")]
    [InlineData("NumPad7", KeyDialect.WinForms, "NumPad7")]
    [InlineData("X", KeyDialect.Bare, "X")]
    [InlineData("F9", KeyDialect.Bare, "F9")]
    public void A_token_survives_a_round_trip_in_its_own_dialect(
        string raw, KeyDialect dialect, string expected)
    {
        var parsed = KeyTokens.Parse(raw, dialect);

        KeyTokens.Format(parsed, dialect).Should().Be(expected);
    }

    [Fact]
    public void The_bare_dialect_writes_number_row_digits_without_the_prefix()
    {
        // Stop The Ped writes D9; several others write a plain 9 for the same
        // key. Writing D9 back to a mod expecting 9 silently unbinds it.
        var token = KeyTokens.Parse("9");

        KeyTokens.Format(token, KeyDialect.Bare).Should().Be("9");
        KeyTokens.Format(token, KeyDialect.WinForms).Should().Be("D9");
    }

    [Fact]
    public void Unbound_writes_back_as_None_in_every_dialect()
    {
        foreach (var dialect in Enum.GetValues<KeyDialect>())
        {
            KeyTokens.Format(KeyToken.None, dialect).Should().Be("None");
        }
    }

    [Fact]
    public void An_unknown_token_is_preserved_rather_than_discarded()
    {
        // A mod may add a key this build has never seen. Dropping it would
        // silently unbind whatever the user had set.
        var parsed = KeyTokens.Parse("SomeFutureKey");

        parsed.Canonical.Should().Be("SomeFutureKey");
        KeyTokens.Format(parsed).Should().Be("SomeFutureKey");
    }

    // ===== Properties the UI depends on ===================================

    [Theory]
    [InlineData("NumPad1", true)]
    [InlineData("NumPad7", true)]
    [InlineData("D1", false)]
    [InlineData("X", false)]
    public void Numpad_keys_are_identifiable_for_the_num_lock_warning(string token, bool expected) =>
        new KeyToken(token).IsNumpad.Should().Be(expected);

    [Theory]
    [InlineData("DPadRight", true)]
    [InlineData("RightThumb", true)]
    [InlineData("LeftTrigger", true)]
    [InlineData("PadX", true)]
    [InlineData("F9", false)]
    [InlineData("NumPad7", false)]
    [InlineData("X", false)]
    [InlineData("A", false)]
    public void Controller_inputs_are_identifiable(string token, bool expected) =>
        new KeyToken(token).IsControllerInput.Should().Be(expected);

    [Fact]
    public void A_keyboard_letter_is_not_a_face_button()
    {
        // A, B, X and Y are both keyboard letters and gamepad face buttons.
        // Without distinct tokens, every keyboard bind on X reported itself as
        // a controller input and the two devices stopped being separable.
        var keyboardX = KeyTokens.Parse("X");
        var padX = KeyTokens.Parse("X", KeyDialect.Controller);

        keyboardX.Should().NotBe(padX);
        keyboardX.IsControllerInput.Should().BeFalse();
        padX.IsControllerInput.Should().BeTrue();
    }

    [Fact]
    public void Face_buttons_read_as_buttons_and_write_back_bare()
    {
        // The Pad prefix is ours; the config file expects the plain letter.
        var padA = KeyTokens.Parse("A", KeyDialect.Controller);

        KeyTokens.ToDisplay(padA).Should().Be("A Button");
        KeyTokens.Format(padA, KeyDialect.Controller).Should().Be("A");
    }

    // ===== Modifiers ======================================================

    [Fact]
    public void A_modified_binding_reads_as_a_chord()
    {
        var binding = new KeyBinding(KeyTokens.Parse("X"), KeyModifier.Shift);

        binding.Display.Should().Be("Left Shift + X");
    }

    [Fact]
    public void Modifiers_read_in_a_stable_order()
    {
        // Control, Shift, Alt regardless of how the flags were combined, so
        // the same chord never appears written two ways.
        var binding = new KeyBinding(
            KeyTokens.Parse("D"),
            KeyModifier.Alt | KeyModifier.Control | KeyModifier.Shift);

        binding.Display.Should().Be("Left Control + Left Shift + Left Alt + D");
    }

    [Fact]
    public void A_modified_binding_is_not_equal_to_the_bare_key()
    {
        // This is what stops the keyboard map reporting Left Shift + X as
        // conflicting with a bare X.
        var bare = new KeyBinding(KeyTokens.Parse("X"));
        var modified = new KeyBinding(KeyTokens.Parse("X"), KeyModifier.Shift);

        modified.Should().NotBe(bare);
    }

    [Fact]
    public void An_unbound_binding_reads_as_unbound_whatever_its_modifier()
    {
        new KeyBinding(KeyToken.None, KeyModifier.Shift).Display.Should().Be("Unbound");
    }
}
