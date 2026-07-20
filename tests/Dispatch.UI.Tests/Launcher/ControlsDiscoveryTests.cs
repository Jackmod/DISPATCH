using Avalonia.Headless.XUnit;
using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The controls screen reads the real ini files on open and folds in keybinds from
/// mods the catalogue has never heard of — keyboard binds on the keyboard tab,
/// controller binds on the controller tab — and edits to them write back to the ini.
/// </summary>
public sealed class ControlsDiscoveryTests
{
    private static string NewGameFolder(string relative, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dispatch-controls-{Guid.NewGuid():N}");
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return root;
    }

    [AvaloniaFact]
    public async Task Discovered_binds_appear_on_the_right_device_tab()
    {
        var root = NewGameFolder("plugins/MysteryMod.ini",
            "MegaMenuKey = F8\nMegaMenuButton = DPadRight\nSomeToggle = true\n");

        try
        {
            var model = new ControlsViewModel(gamePath: root);
            await model.EnsureLoadedAsync();

            model.Device = InputDevice.Keyboard;
            model.Rows.Should().Contain(r => r.Action.ConfigKey == "MegaMenuKey");
            model.Rows.Should().NotContain(r => r.Action.ConfigKey == "MegaMenuButton");

            model.Device = InputDevice.Controller;
            model.Rows.Should().Contain(r => r.Action.ConfigKey == "MegaMenuButton");

            // The mod is now a filter option; its plain setting never became a bind.
            model.PluginFilters.Should().Contain("Mystery Mod");
            model.Rows.Should().NotContain(r => r.Action.ConfigKey == "SomeToggle");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Editing_a_discovered_bind_writes_it_back_to_the_ini()
    {
        var root = NewGameFolder("plugins/MysteryMod.ini", "MegaMenuKey = F8\n");

        try
        {
            var model = new ControlsViewModel(gamePath: root);
            await model.EnsureLoadedAsync();

            var row = model.Rows.First(r => r.Action.ConfigKey == "MegaMenuKey");
            row.IsChanged.Should().BeFalse("the on-disk value is the baseline");

            row.Binding = new KeyBinding(new KeyToken("G"), KeyModifier.Control);
            model.HasPending.Should().BeTrue();

            await model.ApplyCommand.ExecuteAsync(null);

            var document = await IniDocument.LoadAsync(Path.Combine(root, "plugins", "MysteryMod.ini"));
            document.GetAnywhere("MegaMenuKey").Should().Be("G");
            document.GetAnywhere("MegaMenuKeyModifier").Should().Be("Left Control");
            model.HasPending.Should().BeFalse("applying accepts the new baseline");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Loading_twice_does_not_duplicate_discovered_binds()
    {
        var root = NewGameFolder("plugins/MysteryMod.ini", "MegaMenuKey = F8\n");

        try
        {
            var model = new ControlsViewModel(gamePath: root);
            await model.EnsureLoadedAsync();
            await model.EnsureLoadedAsync();
            await model.ReadFromGameCommand.ExecuteAsync(null);

            model.AllBindings.Count(b => b.Action.ConfigKey == "MegaMenuKey").Should().Be(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
