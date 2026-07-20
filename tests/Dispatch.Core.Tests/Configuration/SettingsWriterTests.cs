using Dispatch.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The settings writer edits mod config files in place. The promises are the same
/// as the control writer's: it writes each mod's own literal, backs every file up
/// first, reports a missing file rather than inventing one, and a value survives a
/// write-then-read round trip.
/// </summary>
public sealed class SettingsWriterTests : IDisposable
{
    private readonly string _root;
    private readonly SettingsWriter _writer = new();

    public SettingsWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-sw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ModSetting Setting(string id) => SettingsCatalogue.Settings.First(s => s.Id == id);

    private IReadOnlyList<SettingValue> Sample() =>
    [
        new(Setting("lspdfr.preloadModels"), Setting("lspdfr.preloadModels").OnLiteral),  // "true"
        new(Setting("stp.nearestCop"), Setting("stp.nearestCop").OffLiteral),             // "NO"
        new(Setting("ci.mdtScale"), "80"),                                                // number
        new(Setting("gp.agency"), "\"CUSTOM\""),                                          // quoted text
        new(Setting("hud.timeFormat"), "24"),                                             // choice
    ];

    private void CreateFilesFor(IEnumerable<SettingValue> values)
    {
        foreach (var file in values.Select(v => v.Setting.ConfigFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "; fixture config\n");
        }
    }

    [Fact]
    public async Task Values_survive_a_write_then_read_round_trip()
    {
        var values = Sample();
        CreateFilesFor(values);

        await _writer.WriteAsync(_root, values);
        var readBack = await _writer.ReadAsync(_root, values.Select(v => v.Setting).ToList());

        foreach (var value in values)
        {
            readBack.Should().ContainKey(value.Setting.Id);
            readBack[value.Setting.Id].Should().Be(value.Raw, "the value written for {0} must read back", value.Setting.Id);
        }
    }

    [Fact]
    public async Task Each_written_file_is_backed_up_first()
    {
        var values = Sample();
        CreateFilesFor(values);

        var lspdfrFile = Path.Combine(_root, "lspdfr", "LSPDFR Configuration Setting.ini");
        var before = await File.ReadAllTextAsync(lspdfrFile);

        await _writer.WriteAsync(_root, values);

        var backup = lspdfrFile + ".bak";
        File.Exists(backup).Should().BeTrue();
        (await File.ReadAllTextAsync(backup)).Should().Be(before);
    }

    [Fact]
    public async Task A_toggle_writes_the_mods_own_literal()
    {
        var values = Sample();
        CreateFilesFor(values);

        await _writer.WriteAsync(_root, values);

        // Stop The Ped speaks YES/NO, LSPDFR speaks true/false. Each keeps its own.
        var stp = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "StopThePed.ini"));
        stp.GetAnywhere("UseNearestCopAsTransport").Should().Be("NO");

        var lspdfr = await IniDocument.LoadAsync(Path.Combine(_root, "lspdfr", "LSPDFR Configuration Setting.ini"));
        lspdfr.GetAnywhere("PreloadModels").Should().Be("true");
    }

    [Fact]
    public async Task A_quoted_text_value_keeps_its_quotes()
    {
        var values = Sample();
        CreateFilesFor(values);

        await _writer.WriteAsync(_root, values);

        var gp = await IniDocument.LoadAsync(
            Path.Combine(_root, "plugins", "LSPDFR", "GrammarPolice", "custom", "config.ini"));
        gp.GetAnywhere("Agency").Should().Be("\"CUSTOM\"");
    }

    [Fact]
    public async Task A_missing_file_is_reported_rather_than_created()
    {
        var values = Sample();
        // Create nothing.

        var result = await _writer.WriteAsync(_root, values);

        result.Changes.Should().BeEmpty();
        result.MissingFiles.Should().Contain("lspdfr/LSPDFR Configuration Setting.ini");
    }

    [Fact]
    public async Task A_second_apply_with_no_edits_changes_nothing()
    {
        var values = Sample();
        CreateFilesFor(values);

        await _writer.WriteAsync(_root, values);
        var second = await _writer.WriteAsync(_root, values);

        second.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_reports_changes_without_writing()
    {
        var values = Sample();
        CreateFilesFor(values);

        var path = Path.Combine(_root, "scripts", "SimpleHUD.ini");
        var before = await File.ReadAllTextAsync(path);

        var preview = await _writer.PreviewAsync(_root, values);

        preview.Changes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        File.Exists(path + ".bak").Should().BeFalse();
    }
}
