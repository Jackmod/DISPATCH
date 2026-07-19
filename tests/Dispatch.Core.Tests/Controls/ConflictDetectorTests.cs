using Dispatch.Core.Controls;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Controls;

/// <summary>
/// Conflict detection is the argument for the controls screen existing, so
/// these tests care as much about false positives as about misses. Reporting a
/// working scheme as broken sends people editing config files by hand, which
/// is the exact situation the app is meant to end.
/// </summary>
public sealed class ConflictDetectorTests
{
    private static GameAction Action(
        string id,
        string name = "Do a thing",
        InputDevice device = InputDevice.Keyboard,
        string plugin = "LSPDFR") =>
        new(id, name, "Does a thing in game.", plugin, "General", device,
            "lspdfr/Keys.ini", id.ToUpperInvariant());

    private static BoundAction Bound(string id, string key, KeyModifier modifier = KeyModifier.None,
        InputDevice device = InputDevice.Keyboard) =>
        new(Action(id, device: device), new KeyBinding(KeyTokens.Parse(key), modifier));

    // ===== Finding real conflicts =========================================

    [Fact]
    public void Two_actions_on_one_key_conflict()
    {
        var conflicts = ConflictDetector.Detect([Bound("arrest", "F9"), Bound("patdown", "F9")]);

        conflicts.Should().ContainSingle();
        conflicts[0].Count.Should().Be(2);
        conflicts[0].Binding.Key.Canonical.Should().Be("F9");
    }

    [Fact]
    public void Three_actions_on_one_key_are_reported_once_not_twice()
    {
        // Otherwise the header chip counts pairs and says "3 conflicts" for
        // what a person sees as one contested key.
        var conflicts = ConflictDetector.Detect(
            [Bound("a", "X"), Bound("b", "X"), Bound("c", "X")]);

        conflicts.Should().ContainSingle();
        conflicts[0].Count.Should().Be(3);
    }

    [Fact]
    public void The_summary_names_every_action_involved()
    {
        var conflicts = ConflictDetector.Detect(
        [
            new BoundAction(Action("arrest", "Arrest suspect"), new KeyBinding(KeyTokens.Parse("F9"))),
            new BoundAction(Action("patdown", "Pat down"), new KeyBinding(KeyTokens.Parse("F9"))),
        ]);

        conflicts[0].Summary.Should().Contain("Arrest suspect").And.Contain("Pat down");
    }

    // ===== Not reporting false conflicts ==================================

    [Fact]
    public void Unbound_actions_never_conflict()
    {
        // Thirty actions set to None are thirty things nobody bound, not a
        // thirty-way conflict.
        var conflicts = ConflictDetector.Detect(
            [Bound("a", "None"), Bound("b", "None"), Bound("c", "None")]);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void A_modifier_makes_a_binding_distinct()
    {
        // Left Shift + X against a bare X is the case the keyboard map's
        // layers exist for.
        var conflicts = ConflictDetector.Detect(
            [Bound("delete", "D", KeyModifier.Shift), Bound("duck", "D")]);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Different_modifiers_on_one_key_do_not_conflict()
    {
        var conflicts = ConflictDetector.Detect(
        [
            Bound("clipboard", "T", KeyModifier.Control),
            Bound("notepad", "T", KeyModifier.Shift),
            Bound("talk", "T"),
        ]);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void The_same_modifier_on_one_key_does_conflict()
    {
        var conflicts = ConflictDetector.Detect(
        [
            Bound("clipboard", "T", KeyModifier.Control),
            Bound("notepad", "T", KeyModifier.Control),
        ]);

        conflicts.Should().ContainSingle();
    }

    [Fact]
    public void Keyboard_and_controller_are_separate_namespaces()
    {
        // X on the keyboard and X on the gamepad are different inputs.
        // Reporting them as one flags a perfectly good scheme as broken.
        var conflicts = ConflictDetector.Detect(
        [
            Bound("tackle", "X", device: InputDevice.Keyboard),
            Bound("cuff", "X", device: InputDevice.Controller),
        ]);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void The_number_row_does_not_conflict_with_the_numpad()
    {
        var conflicts = ConflictDetector.Detect([Bound("transport", "D9"), Bound("terminal", "NumPad9")]);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void An_action_does_not_conflict_with_itself()
    {
        var single = Bound("arrest", "F9");

        ConflictDetector.IsInConflict(single, [single]).Should().BeFalse();
    }

    [Fact]
    public void Dialect_differences_still_conflict()
    {
        // Stop The Ped writes D9 and another mod writes a bare 9 for the same
        // physical key. If translation did not converge them, the clash would
        // go unreported.
        var conflicts = ConflictDetector.Detect([Bound("transport", "D9"), Bound("other", "9")]);

        conflicts.Should().ContainSingle();
    }

    // ===== Reserved keys ==================================================

    [Fact]
    public void F4_is_reserved_for_the_RagePluginHook_console()
    {
        GameAction.IsReserved(new KeyBinding(KeyTokens.Parse("F4"))).Should().BeTrue();
    }

    [Fact]
    public void A_modified_F4_is_not_reserved()
    {
        // Only the bare key opens the console.
        GameAction.IsReserved(new KeyBinding(KeyTokens.Parse("F4"), KeyModifier.Shift))
            .Should().BeFalse();
    }

    // ===== Suggesting a way out ===========================================

    [Fact]
    public void A_suggested_key_is_actually_free()
    {
        var bindings = new[] { Bound("a", "F5"), Bound("b", "F6"), Bound("c", "F7") };

        var suggestion = ConflictDetector.SuggestFree(bindings);

        suggestion.IsUnbound.Should().BeFalse();
        bindings.Should().NotContain(b => b.Binding == suggestion);
    }

    [Fact]
    public void A_suggestion_never_offers_the_console_key()
    {
        var suggestion = ConflictDetector.SuggestFree([]);

        GameAction.IsReserved(suggestion).Should().BeFalse();
    }

    [Fact]
    public void Suggestions_respect_the_device()
    {
        var suggestion = ConflictDetector.SuggestFree([], InputDevice.Controller);

        suggestion.Key.IsControllerInput.Should().BeTrue();
    }

    [Fact]
    public void A_suggestion_ignores_bindings_on_the_other_device()
    {
        // A key taken on the gamepad says nothing about the keyboard.
        var bindings = Enumerable.Range(5, 8)
            .Select(i => Bound($"c{i}", $"F{i}", device: InputDevice.Controller))
            .ToList();

        var suggestion = ConflictDetector.SuggestFree(bindings);

        suggestion.Key.Canonical.Should().Be("F5", "the controller bindings do not occupy F5");
    }

    [Fact]
    public void An_exhausted_pool_yields_unbound_rather_than_a_wrong_answer()
    {
        // Better to offer nothing than to hand back a key that is already
        // taken and create a new conflict while resolving one.
        var everything = ConflictDetector.SuggestFree([]);
        var taken = new List<BoundAction>();
        var id = 0;

        var current = everything;
        while (!current.IsUnbound && taken.Count < 200)
        {
            taken.Add(new BoundAction(Action($"a{id++}"), current));
            current = ConflictDetector.SuggestFree(taken);
        }

        current.IsUnbound.Should().BeTrue();
        ConflictDetector.Detect(taken).Should().BeEmpty("every suggestion was distinct");
    }

    [Fact]
    public void An_empty_scheme_has_no_conflicts()
    {
        ConflictDetector.Detect([]).Should().BeEmpty();
    }
}
