using Avalonia.Headless.XUnit;
using Dispatch.Core.Controls;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The click-a-key mapping panel: selecting a key opens it, it lists what is on
/// that key and every action that could go there, and picking one moves the
/// binding onto the key with the current layer's modifier.
/// </summary>
public sealed class ControlsMappingTests
{
    [AvaloniaFact]
    public void Selecting_a_key_opens_the_panel_and_lists_whats_on_it()
    {
        var model = new ControlsViewModel();

        // In the Suggested scheme, Arrest suspect is on E.
        model.SelectedKey = KeyTokens.Parse("E");

        model.IsKeyPanelOpen.Should().BeTrue();
        model.SelectedKeyDisplay.Should().Be("E");
        model.ActionsOnSelectedKey.Should().Contain(r => r.Action.Id == "lspdfr.arrest");
        model.SelectedKeyHasActions.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Mapping_an_action_moves_its_binding_onto_the_selected_key()
    {
        var model = new ControlsViewModel();

        // F8 is unused in the Suggested scheme.
        model.SelectedKey = KeyTokens.Parse("F8");
        model.SelectedKeyHasActions.Should().BeFalse();

        var arrest = model.AssignableActions.First(r => r.Action.Id == "lspdfr.arrest");
        model.MapToKeyCommand.Execute(arrest);

        arrest.Binding.Key.Canonical.Should().Be("F8");
        model.ActionsOnSelectedKey.Should().Contain(r => r.Action.Id == "lspdfr.arrest");
    }

    [AvaloniaFact]
    public void The_assignable_list_excludes_whats_already_on_the_key_and_honours_search()
    {
        var model = new ControlsViewModel();
        model.SelectedKey = KeyTokens.Parse("E");

        // Arrest is already on E, so it should not be offered as assignable.
        model.AssignableActions.Should().NotContain(r => r.Action.Id == "lspdfr.arrest");

        model.KeyPanelSearch = "spotlight";
        model.AssignableActions.Should().OnlyContain(r =>
            r.Action.Name.Contains("spotlight", System.StringComparison.OrdinalIgnoreCase) ||
            r.Action.Plugin.Contains("spotlight", System.StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void The_controller_map_uses_the_same_panel_and_mapping()
    {
        var model = new ControlsViewModel { Device = InputDevice.Controller };

        // Tackle is on the X button in the Suggested scheme.
        model.SelectedKey = KeyTokens.Parse("PadX", KeyDialect.Controller);

        model.IsKeyPanelOpen.Should().BeTrue();
        model.ActionsOnSelectedKey.Should().Contain(r => r.Action.Id == "pad.tackle");

        var backup = model.AssignableActions.First(r => r.Action.Id == "pad.backup");
        model.MapToKeyCommand.Execute(backup);

        backup.Binding.Key.Canonical.Should().Be("PadX");
    }

    [AvaloniaFact]
    public void Closing_the_panel_clears_the_selected_key()
    {
        var model = new ControlsViewModel();
        model.SelectedKey = KeyTokens.Parse("E");

        model.ClosePanelCommand.Execute(null);

        model.IsKeyPanelOpen.Should().BeFalse();
        model.SelectedKey.Should().BeNull();
    }
}
