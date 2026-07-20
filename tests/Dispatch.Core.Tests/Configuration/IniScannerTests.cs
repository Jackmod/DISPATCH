using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The scanner is what makes a plugin the app has never heard of editable: it
/// reads whatever ini files a game folder holds and turns each key into a named,
/// typed setting. It must find keys wherever they live, read them as the right
/// shape, name them readably, and — through the writer — round-trip.
/// </summary>
public sealed class IniScannerTests : IDisposable
{
    private readonly string _root;
    private readonly IniScanner _scanner = new();

    public IniScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task It_finds_settings_in_a_brand_new_plugin_folder()
    {
        Write("plugins/NewCoolMod.ini",
            "[General]\n" +
            "EnableSirens = true\n" +
            "MaxUnits = 4\n" +
            "AgencyName = Los Santos PD\n");

        var found = await _scanner.ScanAsync(_root);

        found.Should().HaveCount(3);
        found.Should().OnlyContain(s => s.Discovered);
        found.Select(s => s.ConfigKey).Should().Contain(new[] { "EnableSirens", "MaxUnits", "AgencyName" });
    }

    [Fact]
    public async Task It_infers_the_editor_from_the_value()
    {
        Write("scripts/Thing.ini",
            "Toggle = YES\n" +
            "Count = 12\n" +
            "Ratio = 0.5\n" +
            "Label = hello\n");

        var found = await _scanner.ScanAsync(_root);

        found.First(s => s.ConfigKey == "Toggle").Kind.Should().Be(SettingKind.Toggle);
        found.First(s => s.ConfigKey == "Count").Kind.Should().Be(SettingKind.Number);
        found.First(s => s.ConfigKey == "Ratio").Kind.Should().Be(SettingKind.Number);
        found.First(s => s.ConfigKey == "Label").Kind.Should().Be(SettingKind.Text);
    }

    [Fact]
    public async Task A_discovered_toggle_keeps_the_files_own_yes_no_casing()
    {
        Write("plugins/M.ini", "UseLights = YES\n");

        var toggle = (await _scanner.ScanAsync(_root)).First();

        toggle.OnLiteral.Should().Be("YES");
        toggle.OffLiteral.Should().Be("NO");
        toggle.ParseBool(toggle.Default).Should().BeTrue();
    }

    [Theory]
    [InlineData("InitialSpeedThreshold", "Initial speed threshold")]
    [InlineData("MDTPositionX", "MDT position X")]
    [InlineData("use_siren", "Use siren")]
    public async Task Keys_become_readable_names(string key, string expected)
    {
        // A plain numeric value keeps these as settings; a key-shaped name would be
        // read as a bind instead, which is covered separately.
        Write("plugins/M.ini", $"{key} = 42\n");

        var setting = (await _scanner.ScanAsync(_root)).First();

        setting.Name.Should().Be(expected);
    }

    [Fact]
    public async Task It_records_the_section_so_a_duplicated_key_edits_the_right_one()
    {
        Write("plugins/LSPDFR/GrammarPolice/custom/config.ini",
            "[default]\nCallSign = 1 ADAM 7\n\n[custom]\nCallSign = 2 LINCOLN 14\n");

        var found = await _scanner.ScanAsync(_root);

        found.Should().Contain(s => s.ConfigKey == "CallSign" && s.Section == "default");
        found.Should().Contain(s => s.ConfigKey == "CallSign" && s.Section == "custom");

        // The plugin name comes from the folder, not the generic "config" file name.
        found.Should().OnlyContain(s => s.Plugin == "Grammar Police");
    }

    [Fact]
    public async Task It_skips_our_own_backup_files()
    {
        Write("plugins/M.ini", "Volume = 1\n");
        Write("plugins/M.ini.bak", "Volume = 999\n");

        var found = await _scanner.ScanAsync(_root);

        found.Should().ContainSingle();
    }

    [Fact]
    public async Task A_discovered_setting_round_trips_through_the_writer()
    {
        Write("plugins/NewMod.ini", "[Opt]\nSomeFlag = false\n");
        var writer = new SettingsWriter();

        var setting = (await _scanner.ScanAsync(_root)).First(s => s.ConfigKey == "SomeFlag");

        // Flip it on, using the discovered literal, and confirm it lands in the file.
        await writer.WriteAsync(_root, [new SettingValue(setting, setting.OnLiteral)]);

        var document = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "NewMod.ini"));
        document.Get("Opt", "SomeFlag").Should().Be("true");
    }

    [Fact]
    public async Task An_empty_or_absent_folder_scans_to_nothing_rather_than_throwing()
    {
        (await _scanner.ScanAsync(_root)).Should().BeEmpty();
        (await _scanner.ScanAsync(Path.Combine(_root, "does-not-exist"))).Should().BeEmpty();
    }

    // ===== Keybind classification ========================================

    [Fact]
    public async Task One_ini_splits_across_keyboard_controller_and_settings()
    {
        // A single file with a keyboard bind, a controller bind and a plain setting:
        // each must land in the right place, which is the whole point.
        Write("plugins/SomeMod.ini",
            "[General]\n" +
            "OpenMenuKey = F6\n" +
            "OpenMenuButton = DPadRight\n" +
            "EnableFeature = true\n");

        var scan = await _scanner.ScanAllAsync(_root);

        scan.Keybinds.Should().Contain(k =>
            k.Action.ConfigKey == "OpenMenuKey" && k.Action.Device == InputDevice.Keyboard);
        scan.Keybinds.Should().Contain(k =>
            k.Action.ConfigKey == "OpenMenuButton" && k.Action.Device == InputDevice.Controller);
        scan.Settings.Should().Contain(s => s.ConfigKey == "EnableFeature" && s.Kind == SettingKind.Toggle);

        // And the binds never leak into the settings list.
        scan.Settings.Should().NotContain(s => s.ConfigKey == "OpenMenuKey" || s.ConfigKey == "OpenMenuButton");
        (await _scanner.ScanAsync(_root)).Should().NotContain(s => s.ConfigKey == "OpenMenuKey" || s.ConfigKey == "OpenMenuButton");
    }

    [Fact]
    public async Task A_discovered_keyboard_bind_reads_its_value_and_name()
    {
        Write("plugins/M.ini", "PatDownKey = F9\n");

        var bind = (await _scanner.ScanAllAsync(_root)).Keybinds.Single();

        bind.Action.Name.Should().Be("Pat down");
        bind.Action.Device.Should().Be(InputDevice.Keyboard);
        bind.Binding.Key.Canonical.Should().Be("F9");
    }

    [Fact]
    public async Task A_key_named_bind_holding_a_bool_stays_a_setting()
    {
        // "MonitorKey = true" looks like a bind by name but its value is a bool, so it
        // must stay an editable toggle, never a keybind.
        Write("plugins/M.ini", "MonitorKey = true\n");

        var scan = await _scanner.ScanAllAsync(_root);

        scan.Keybinds.Should().BeEmpty();
        scan.Settings.Should().ContainSingle(s => s.ConfigKey == "MonitorKey" && s.Kind == SettingKind.Toggle);
    }

    [Fact]
    public async Task A_controller_bind_is_recognised_by_its_value_alone()
    {
        // No "Button" suffix, but the value is unmistakably a D-pad direction.
        Write("plugins/M.ini", "Pursuit = DPadDown\n");

        var bind = (await _scanner.ScanAllAsync(_root)).Keybinds.Single();

        bind.Action.Device.Should().Be(InputDevice.Controller);
        bind.Binding.Key.Canonical.Should().Be("DPadDown");
    }

    [Fact]
    public async Task A_bind_folds_in_its_modifier_companion()
    {
        Write("lspdfr/Keys.ini", "TRAFFIC_STOP_Key = X\nTRAFFIC_STOP_KeyModifier = Left Shift\n");

        var scan = await _scanner.ScanAllAsync(_root);

        var bind = scan.Keybinds.Single(k => k.Action.ConfigKey == "TRAFFIC_STOP_Key");
        bind.Binding.Key.Canonical.Should().Be("X");
        bind.Binding.Modifier.Should().Be(KeyModifier.Shift);

        // The companion is absorbed, not shown as its own setting or bind.
        scan.Settings.Should().NotContain(s => s.ConfigKey == "TRAFFIC_STOP_KeyModifier");
        scan.Keybinds.Should().NotContain(k => k.Action.ConfigKey == "TRAFFIC_STOP_KeyModifier");
    }

    [Fact]
    public async Task A_discovered_keyboard_bind_round_trips_through_the_control_writer()
    {
        Write("plugins/M.ini", "SomeActionKey = F9\n");
        var writer = new ControlWriter();

        var bind = (await _scanner.ScanAllAsync(_root)).Keybinds.Single();

        // Rebind to Left Control + G and confirm the file holds the new value and modifier.
        var moved = new BoundAction(bind.Action, new KeyBinding(new KeyToken("G"), KeyModifier.Control));
        await writer.WriteAsync(_root, [moved]);

        var document = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "M.ini"));
        document.GetAnywhere("SomeActionKey").Should().Be("G");
        document.GetAnywhere("SomeActionKeyModifier").Should().Be("Left Control");
    }

    [Fact]
    public async Task A_bare_number_row_bind_keeps_its_bare_spelling_on_write()
    {
        // Written bare as "9"; a rebind to another number-row key must stay bare (not "D8").
        Write("plugins/M.ini", "MenuKey = 9\n");
        var writer = new ControlWriter();

        var bind = (await _scanner.ScanAllAsync(_root)).Keybinds.Single();
        bind.Binding.Key.Canonical.Should().Be("D9");

        var moved = new BoundAction(bind.Action, new KeyBinding(new KeyToken("D8")));
        await writer.WriteAsync(_root, [moved]);

        var document = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "M.ini"));
        document.GetAnywhere("MenuKey").Should().Be("8");
    }
}
