using Dispatch.Core.Configuration;
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
    [InlineData("PatDownKey", "Pat down")]
    [InlineData("InitialSpeedThreshold", "Initial speed threshold")]
    [InlineData("MDTPositionX", "MDT position X")]
    [InlineData("use_siren", "Use siren")]
    public async Task Keys_become_readable_names(string key, string expected)
    {
        Write("plugins/M.ini", $"{key} = 1\n");

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
        Write("plugins/M.ini", "Key = 1\n");
        Write("plugins/M.ini.bak", "Key = 999\n");

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
}
