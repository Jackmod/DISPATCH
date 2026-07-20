using Avalonia.Headless.XUnit;
using Dispatch.Core.Configuration;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The plugin-settings screen: it lists every setting grouped by plugin, stages
/// edits until Apply, narrows on search, and on Apply writes the real ini.
/// </summary>
public sealed class PluginSettingsViewModelTests
{
    [AvaloniaFact]
    public void Opens_populated_with_nothing_staged()
    {
        var model = new PluginSettingsViewModel();

        model.Groups.Should().NotBeEmpty();
        model.HasPending.Should().BeFalse();
        model.PendingCount.Should().Be(0);
    }

    [AvaloniaFact]
    public void Editing_a_setting_stages_it()
    {
        var model = new PluginSettingsViewModel();
        var toggle = model.Groups.SelectMany(g => g.Rows).First(r => r.IsToggle);

        toggle.BoolValue = !toggle.BoolValue;

        model.HasPending.Should().BeTrue();
        model.PendingCount.Should().Be(1);
        model.PendingDiff.Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Discard_reverts_every_staged_edit()
    {
        var model = new PluginSettingsViewModel();
        var toggle = model.Groups.SelectMany(g => g.Rows).First(r => r.IsToggle);
        toggle.BoolValue = !toggle.BoolValue;

        model.DiscardCommand.Execute(null);

        model.HasPending.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Search_narrows_to_matching_settings()
    {
        var model = new PluginSettingsViewModel { Search = "dash cam" };

        model.Groups.Should().OnlyContain(g => g.Plugin == "Dash Cam V");
    }

    [AvaloniaFact]
    public async Task Scanning_surfaces_a_new_plugins_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dispatch-ps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "plugins", "BrandNew.ini"),
            "[X]\nCoolFlag = true\nCoolNumber = 5\n");

        try
        {
            var model = new PluginSettingsViewModel(gamePath: root);

            await model.ScanCommand.ExecuteAsync(null);

            var rows = model.Groups.SelectMany(g => g.Rows).ToList();
            rows.Should().Contain(r => r.Setting.ConfigKey == "CoolFlag" && r.Setting.Discovered);
            model.PluginFilters.Should().Contain("Brand New");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Apply_writes_the_change_to_the_real_ini()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dispatch-ps-{Guid.NewGuid():N}");
        var file = Path.Combine(root, "plugins", "StopThePed.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "UseNearestCopAsTransport = YES\n");

        try
        {
            var model = new PluginSettingsViewModel(gamePath: root);
            await model.LoadFromDiskAsync();

            var row = model.Groups.SelectMany(g => g.Rows).First(r => r.Setting.Id == "stp.nearestCop");
            row.BoolValue = false;

            await model.ApplyCommand.ExecuteAsync(null);

            var document = await IniDocument.LoadAsync(file);
            document.GetAnywhere("UseNearestCopAsTransport").Should().Be("NO");
            model.HasPending.Should().BeFalse("applying clears the staged state");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
