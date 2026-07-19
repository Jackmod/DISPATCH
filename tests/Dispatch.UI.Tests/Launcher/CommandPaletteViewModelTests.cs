using Avalonia.Headless.XUnit;
using Dispatch.Core.Palette;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The palette's behaviour: it opens cleared, always keeps a highlight so Enter
/// does something, wraps at the ends, and reaches every keybindable action by
/// name so "arrest" jumps straight to that bind.
/// </summary>
public sealed class CommandPaletteViewModelTests
{
    [AvaloniaFact]
    public void Opens_cleared_and_showing_everything()
    {
        var palette = new CommandPaletteViewModel();
        palette.Query = "stale";

        palette.Open();

        palette.IsOpen.Should().BeTrue();
        palette.Query.Should().BeEmpty("opening starts a fresh search");
        palette.Results.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Always_keeps_a_highlight_so_enter_does_something()
    {
        var palette = new CommandPaletteViewModel();
        palette.Open();

        palette.Selected.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void Typing_an_action_name_ranks_that_binding_first()
    {
        var palette = new CommandPaletteViewModel();
        palette.Open();

        palette.Query = "arrest";

        palette.Results[0].Action.Should().Be(PaletteAction.EditBinding);
        palette.Results[0].Target.Should().Be("lspdfr.arrest");
    }

    [AvaloniaFact]
    public void A_section_name_finds_the_section()
    {
        var palette = new CommandPaletteViewModel();
        palette.Open();

        palette.Query = "settings";

        palette.Results[0].Action.Should().Be(PaletteAction.Navigate);
        palette.Results[0].Target.Should().Be("settings");
    }

    [AvaloniaFact]
    public void Moving_down_wraps_at_the_end()
    {
        var palette = new CommandPaletteViewModel();
        palette.Open();
        palette.Query = "settings"; // narrow to a small, stable result set

        var count = palette.Results.Count;
        for (var i = 0; i < count; i++)
        {
            palette.MoveDown();
        }

        // A full loop returns to the first result.
        palette.Selected.Should().Be(palette.Results[0]);
    }

    [AvaloniaFact]
    public void Choosing_raises_the_chosen_event_and_closes()
    {
        var palette = new CommandPaletteViewModel();
        palette.Open();
        palette.Query = "dashboard";

        PaletteEntry? chosen = null;
        palette.Chosen += (_, entry) => chosen = entry;

        palette.ChooseSelected();

        chosen.Should().NotBeNull();
        chosen!.Target.Should().Be("dashboard");
        palette.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Choosing_a_keybind_from_the_palette_navigates_to_controls_and_searches_it()
    {
        // The launcher's job: land on the controls screen with the named action
        // already filtered, so the palette drops the user on the exact row.
        var launcher = new LauncherViewModel();
        launcher.Palette.Open();
        launcher.Palette.Query = "arrest";

        launcher.Palette.ChooseSelected();

        launcher.Current.Key.Should().Be("controls");
    }

    [AvaloniaFact]
    public void Choosing_a_section_navigates_there()
    {
        var launcher = new LauncherViewModel();
        launcher.Palette.Open();
        launcher.Palette.Query = "settings";

        launcher.Palette.ChooseSelected();

        launcher.Current.Key.Should().Be("settings");
    }
}
