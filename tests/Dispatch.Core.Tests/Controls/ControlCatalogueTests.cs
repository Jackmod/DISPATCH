using Dispatch.Core.Controls;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Controls;

/// <summary>
/// The catalogue is the data the controls screen renders, so the properties
/// that matter are structural: the Suggested scheme must actually be
/// conflict-free, and the Factory scheme must actually contain the conflicts
/// it claims to. Both are load-bearing promises made to the user.
/// </summary>
public sealed class ControlCatalogueTests
{
    [Fact]
    public void The_suggested_scheme_is_genuinely_conflict_free()
    {
        // This is the scheme the wizard preselects and the guide promises. If
        // it ever gains a conflict, the app is shipping the problem it exists
        // to solve.
        var conflicts = ConflictDetector.Detect(ControlCatalogue.Bind(ControlCatalogue.Suggested));

        conflicts.Should().BeEmpty(
            "the preselected scheme is advertised as conflict-free: {0}",
            string.Join("; ", conflicts.Select(c => c.Summary)));
    }

    [Fact]
    public void The_suggested_scheme_never_takes_the_console_key()
    {
        // F4 opens the RagePluginHook console, which is where people go when
        // something has broken. No mod is entitled to take it.
        var reserved = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(bound => GameAction.IsReserved(bound.Binding))
            .ToList();

        reserved.Should().BeEmpty(
            "F4 belongs to the console: {0}",
            string.Join(", ", reserved.Select(r => r.Action.Name)));
    }

    [Fact]
    public void The_factory_scheme_does_contain_conflicts()
    {
        // The Default profile is offered as reference and described as
        // conflicting. If it were clean, that description would be a lie and
        // the Suggested scheme would have no reason to exist.
        var conflicts = ConflictDetector.Detect(ControlCatalogue.Bind(ControlCatalogue.Factory));

        conflicts.Should().NotBeEmpty("Default is documented as containing conflicts");
    }

    [Fact]
    public void No_shipped_scheme_binds_the_reserved_console_key()
    {
        // F4 opens the RagePluginHook console — the way back in when something
        // breaks — so neither scheme may hand it out. No tutorial mod ships on
        // it: Grammar Police's interface key ships on F3 (colliding with Simple
        // Trainer, which is why the tutorial moves it to F8), not F4. The older
        // "Grammar Police ships on F4" belief was a transcription error.
        var onConsole = ControlCatalogue.Bind(ControlCatalogue.Factory)
            .Concat(ControlCatalogue.Bind(ControlCatalogue.Suggested))
            .Where(bound => GameAction.IsReserved(bound.Binding))
            .ToList();

        onConsole.Should().BeEmpty(
            "F4 belongs to the console: {0}",
            string.Join(", ", onConsole.Select(r => r.Action.Name)));
    }

    [Fact]
    public void Every_action_has_a_binding_in_both_schemes()
    {
        // A missing entry silently renders as Unbound, which looks like a
        // deliberate choice rather than an oversight.
        foreach (var action in ControlCatalogue.Actions)
        {
            ControlCatalogue.Suggested.Should().ContainKey(action.Id);
            ControlCatalogue.Factory.Should().ContainKey(action.Id);
        }
    }

    [Fact]
    public void No_scheme_binds_an_action_that_does_not_exist()
    {
        // A stale identifier means a bind that is written to a config file but
        // never shown, which is undebuggable from the UI.
        var ids = ControlCatalogue.Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        ControlCatalogue.Suggested.Keys.Should().BeSubsetOf(ids);
        ControlCatalogue.Factory.Keys.Should().BeSubsetOf(ids);
    }

    [Fact]
    public void Action_identifiers_are_unique()
    {
        ControlCatalogue.Actions.Select(a => a.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_action_explains_itself_in_plain_words()
    {
        // The description is shown on hover and in the detail pane. An empty
        // one leaves the user guessing what a bind does.
        ControlCatalogue.Actions.Should().OnlyContain(a =>
            !string.IsNullOrWhiteSpace(a.Name) &&
            !string.IsNullOrWhiteSpace(a.Description) &&
            a.Description.EndsWith('.'));
    }

    [Fact]
    public void Every_action_names_the_file_and_key_it_writes()
    {
        // Without both, the staged-changes diff cannot say what it is about to
        // edit, and the write has nowhere to go.
        ControlCatalogue.Actions.Should().OnlyContain(a =>
            !string.IsNullOrWhiteSpace(a.ConfigFile) &&
            !string.IsNullOrWhiteSpace(a.ConfigKey));
    }

    [Fact]
    public void Controller_actions_are_bound_to_controller_inputs()
    {
        var controller = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(bound => bound.Action.Device == InputDevice.Controller)
            .Where(bound => !bound.Binding.IsUnbound);

        controller.Should().OnlyContain(bound => bound.Binding.Key.IsControllerInput);
    }

    [Fact]
    public void Keyboard_actions_are_not_bound_to_controller_inputs()
    {
        var keyboard = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(bound => bound.Action.Device == InputDevice.Keyboard)
            .Where(bound => !bound.Binding.IsUnbound);

        keyboard.Should().OnlyContain(bound => !bound.Binding.Key.IsControllerInput);
    }

    [Fact]
    public void The_traffic_stop_keeps_the_de_facto_standard_key()
    {
        // Left Shift for the traffic stop is what everyone who has played
        // before will reach for. Changing it is allowed but warned about;
        // shipping it changed would be gratuitous.
        ControlCatalogue.Suggested["lspdfr.stop"].Key.Canonical.Should().Be("LShiftKey");
    }

    [Fact]
    public void Categories_and_plugins_are_available_for_the_filter_chips()
    {
        ControlCatalogue.Categories.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        ControlCatalogue.Plugins.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Binding_a_scheme_covers_every_action_exactly_once()
    {
        var bound = ControlCatalogue.Bind(ControlCatalogue.Suggested);

        bound.Should().HaveCount(ControlCatalogue.Actions.Count);
        bound.Select(b => b.Action.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void An_empty_scheme_leaves_everything_unbound_rather_than_throwing()
    {
        var bound = ControlCatalogue.Bind(new Dictionary<string, KeyBinding>());

        bound.Should().OnlyContain(b => b.Binding.IsUnbound);
        ConflictDetector.Detect(bound).Should().BeEmpty();
    }
}
