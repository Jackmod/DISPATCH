using System.IO.Compression;
using Dispatch.Core.Acquisition;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Installation;
using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Installation;

/// <summary>
/// The whole bundled pipeline end to end, offline: a mod dropped in the pack is
/// unpacked and placed, and — the guarantee that matters — a mod NOT selected is
/// never touched, even though its archive sits right there in the pack.
/// </summary>
public sealed class SelectiveInstallTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-selective", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly AppPaths _paths;
    private readonly RealInstallRunner _runner;

    public SelectiveInstallTests()
    {
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(_game);

        _paths = new AppPaths(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        _paths.EnsureCreated();

        var extractor = new ArchiveExtractor(NullLogger<ArchiveExtractor>.Instance);
        var bundled = new BundledModSource(
            new ModPackRoots([_paths.ModPackDirectory]), NullLogger<BundledModSource>.Instance);
        var acquirer = new Acquirer([bundled], extractor, _paths, NullLogger<Acquirer>.Instance);

        var placer = new FilePlacer(
            new BackupStore(_paths.BackupsDirectory, NullLogger<BackupStore>.Instance),
            NullLogger<FilePlacer>.Instance);
        var local = new LocalInstallRunner(placer, _paths, NullLogger<LocalInstallRunner>.Instance);

        var config = new Dispatch.Core.Configuration.ConfigInstaller(
            new Dispatch.Core.Configuration.IniConfigWriter(
                NullLogger<Dispatch.Core.Configuration.IniConfigWriter>.Instance),
            NullLogger<Dispatch.Core.Configuration.ConfigInstaller>.Instance);

        _runner = new RealInstallRunner(
            acquirer, local, config, _paths, new RunIdFactory(), NullLogger<RealInstallRunner>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Drops a zip holding the given files into the pack under a mod id.</summary>
    private void DropInPack(string modId, params (string Entry, string Content)[] entries)
    {
        var dir = Path.Combine(_paths.ModPackDirectory, modId);
        Directory.CreateDirectory(dir);

        using var stream = File.Create(Path.Combine(dir, $"{modId}.zip"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = archive.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open());
            writer.Write(content);
        }
    }

    private bool InGame(string relative) =>
        File.Exists(Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task Only_the_selected_mod_is_unpacked_even_though_others_sit_in_the_pack()
    {
        // Two mods are in the pack. Simple Trainer places named files into the
        // game root; Stop The Ped would place a whole plugins tree.
        DropInPack("simpletrainer", ("TrainerV.asi", "trainer"), ("TrainerV.ini", "cfg"));
        DropInPack("stoptheped", ("plugins/StopThePed.dll", "stp"), ("plugins/StopThePed.ini", "stpcfg"));

        var progress = new Progress<InstallProgress>(_ => { });

        // The user ticked ONLY Simple Trainer.
        var request = new InstallRequest(
            _game, "Custom", ModCount: 1, PresetId: "full-duty", ModIds: ["simpletrainer"]);

        var report = await _runner.RunAsync(request, progress, CancellationToken.None);

        // Simple Trainer landed.
        InGame("TrainerV.asi").Should().BeTrue();
        InGame("TrainerV.ini").Should().BeTrue();
        report.Installed.Should().ContainSingle().Which.Should().Be("Simple Trainer");

        // Stop The Ped was never touched, though its archive was right there.
        InGame("plugins/StopThePed.dll").Should().BeFalse();
        Directory.Exists(Path.Combine(_game, "plugins")).Should().BeFalse();
        report.Installed.Should().NotContain("Stop The Ped");
    }

    [Fact]
    public async Task A_selected_mod_absent_from_the_pack_is_reported_as_needing_it()
    {
        // Nothing dropped in the pack; the selected mod cannot be found.
        var request = new InstallRequest(
            _game, "Custom", ModCount: 1, PresetId: "full-duty", ModIds: ["stoptheped"]);

        var report = await _runner.RunAsync(request, new Progress<InstallProgress>(_ => { }), CancellationToken.None);

        report.Installed.Should().BeEmpty();
        report.Skipped.Should().ContainSingle().Which.Mod.Should().Be("Stop The Ped");
        report.Skipped[0].Reason.Should().Contain("stoptheped");
    }
}
