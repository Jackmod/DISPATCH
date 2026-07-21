using Dispatch.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The config installer end to end: find each mod's config file in the game
/// folder, write the guide's values into it, fill the officer's details.
/// </summary>
public sealed class ConfigInstallerTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-config", Guid.NewGuid().ToString("N"));

    private readonly ConfigInstaller _installer =
        new(new IniConfigWriter(NullLogger<IniConfigWriter>.Instance), NullLogger<ConfigInstaller>.Instance);

    public ConfigInstallerTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        try { if (Directory.Exists(_game)) Directory.Delete(_game, recursive: true); }
        catch (IOException) { }
    }

    private void WriteConfig(string relative, string content)
    {
        var full = Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string ReadConfig(string relative) =>
        File.ReadAllText(Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task It_finds_the_config_by_glob_and_writes_the_guide_values()
    {
        // The real TrainerV keys: SpawnADriverKey and AddWayPoint (no "Key" suffix).
        WriteConfig("TrainerV.ini",
            "[Trainer]\n" +
            "SpawnADriverKey = 113\n" +
            "AddWayPoint = 114\n");

        var report = await _installer.ApplyAsync(_game, ["simpletrainer"], OfficerValues.Default);

        var trainer = report.Outcomes.Single();
        trainer.File.Should().Be("TrainerV.ini");
        trainer.Applied.Should().Contain("SpawnADriverKey").And.Contain("AddWayPoint");

        ReadConfig("TrainerV.ini").Should().Contain("SpawnADriverKey = 0");
        ReadConfig("TrainerV.ini").Should().Contain("AddWayPoint = 0");
    }

    [Fact]
    public async Task It_fills_the_officer_callsign_and_leaves_a_hand_tuned_comment()
    {
        // Callout Interface, found under plugins/LSPDFR via a hint.
        WriteConfig("plugins/LSPDFR/CalloutInterface.ini",
            "; do not delete this line\n" +
            "MDTCallSign = CHANGEME\n" +
            "CalloutMenuKey = F9\n" +
            "PostalCodeSet = default\n");

        var officer = new OfficerValues("3 LINCOLN 22", "Jack", "LSPD", "AIR 1");
        var report = await _installer.ApplyAsync(_game, ["calloutinterface"], officer);

        report.Outcomes.Single().File.Should().Be("plugins/LSPDFR/CalloutInterface.ini");

        var text = ReadConfig("Plugins/LSPDFR/CalloutInterface.ini");
        text.Should().Contain("MDTCallSign = 3 LINCOLN 22");
        text.Should().Contain("CalloutMenuKey = F10");
        text.Should().Contain("PostalCodeSet = virus_City");
        text.Should().Contain("; do not delete this line", "the file is edited in place");
    }

    [Fact]
    public async Task A_missing_config_file_is_reported_not_invented()
    {
        var report = await _installer.ApplyAsync(_game, ["calloutinterface"], OfficerValues.Default);

        report.FilesNotFound.Should().Contain("calloutinterface");
        Directory.EnumerateFiles(_game, "*", SearchOption.AllDirectories)
            .Should().BeEmpty("no config file is created out of nothing");
    }
}
