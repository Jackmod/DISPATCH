using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Installation;
using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Installation;

/// <summary>
/// The real placement engine, against a fixture. It reads the catalogue's
/// placement rules and does the part that touches a game folder — back up,
/// place, verify, journal, record — so the tests care that the rules are
/// honoured, the record is accurate, and the whole run rolls back cleanly.
/// No network, no real game.
/// </summary>
public sealed class LocalInstallRunnerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-install", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly string _staging;
    private readonly AppPaths _paths;
    private readonly LocalInstallRunner _runner;

    public LocalInstallRunnerTests()
    {
        _game = Path.Combine(_root, "game");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_staging);

        _paths = new AppPaths(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        var placer = new FilePlacer(
            new BackupStore(_paths.BackupsDirectory, NullLogger<BackupStore>.Instance),
            NullLogger<FilePlacer>.Instance);
        _runner = new LocalInstallRunner(placer, _paths, NullLogger<LocalInstallRunner>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private LocalInstallRunner.StagedMod Stage(string modId, params (string Path, string Content)[] files)
    {
        var mod = ModCatalogue.Mods[modId];
        var folder = Path.Combine(_staging, modId);

        foreach (var (path, content) in files)
        {
            var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return new LocalInstallRunner.StagedMod(mod, folder);
    }

    private bool InGame(string relative) =>
        File.Exists(Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar)));

    private string ReadGame(string relative) =>
        File.ReadAllText(Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task Named_files_are_placed_and_others_ignored()
    {
        // Script Hook V takes only dinput8.dll and ScriptHookV.dll.
        var shv = Stage("scripthookv",
            ("dinput8.dll", "a"), ("ScriptHookV.dll", "b"), ("ScriptHookV.ini", "ignore me"), ("readme.txt", "skip"));

        await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", [shv]);

        InGame("dinput8.dll").Should().BeTrue();
        InGame("ScriptHookV.dll").Should().BeTrue();
        InGame("ScriptHookV.ini").Should().BeFalse("only the two named files are placed");
        InGame("readme.txt").Should().BeFalse();
    }

    [Fact]
    public async Task Root_all_excludes_named_files()
    {
        // LSPDFR places everything except its licence and the RPH readme.
        var lspdfr = Stage("lspdfr",
            ("LSPD First Response.dll", "core"),
            ("License.txt", "legal"),
            ("ReadMe - RagePluginHook.txt", "readme"));

        await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", [lspdfr]);

        InGame("LSPD First Response.dll").Should().BeTrue();
        InGame("License.txt").Should().BeFalse("the licence is excluded");
        InGame("ReadMe - RagePluginHook.txt").Should().BeFalse();
    }

    [Fact]
    public async Task Folder_contents_land_at_the_destination()
    {
        // Charges & Citations goes to plugins/lspdfr/Compulite.
        var charges = Stage("charges", ("config.ini", "settings"), ("citations.xml", "data"));

        await _runner.RunAsync("run-1", _game, "full-duty", "1.0.3725", [charges]);

        InGame("plugins/lspdfr/Compulite/config.ini").Should().BeTrue();
        InGame("plugins/lspdfr/Compulite/citations.xml").Should().BeTrue();
    }

    [Fact]
    public async Task A_bundled_ragenativeui_is_stripped_before_placement()
    {
        // Grammar Police ships its own RageNativeUI; the root copy must win, so
        // the bundled one is stripped and never placed. Its archive nests its
        // folders under a "Grand Theft Auto V" folder, which is its content root.
        var grammar = Stage("grammarpolice",
            ("Grand Theft Auto V/plugins/GrammarPolice.dll", "code"),
            ("Grand Theft Auto V/plugins/RageNativeUI.dll", "stale bundled copy"));

        await _runner.RunAsync("run-1", _game, "full-duty", "1.0.3725", [grammar]);

        InGame("plugins/GrammarPolice.dll").Should().BeTrue();
        InGame("plugins/RageNativeUI.dll").Should().BeFalse("the bundled copy is stripped");
    }

    [Fact]
    public async Task A_protected_assembly_is_not_overwritten_by_a_later_mod()
    {
        // Callout Interface's assembly is placed, then Grammar Police ships a
        // copy — which must be left alone.
        Directory.CreateDirectory(Path.Combine(_game, "plugins"));
        File.WriteAllText(Path.Combine(_game, "plugins", "CalloutInterface.ApplicationExtension.dll"), "the real one");

        var grammar = Stage("grammarpolice",
            ("CalloutInterface.ApplicationExtension.dll", "Grammar Police's stale copy"));

        await _runner.RunAsync("run-1", _game, "full-duty", "1.0.3725", [grammar]);

        ReadGame("plugins/CalloutInterface.ApplicationExtension.dll")
            .Should().Be("the real one", "the protected assembly is never overwritten");
    }

    [Fact]
    public async Task The_install_record_hashes_every_placed_file()
    {
        var shv = Stage("scripthookv", ("dinput8.dll", "a"), ("ScriptHookV.dll", "b"));

        var record = await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", [shv]);

        record.Files.Should().HaveCount(2);
        record.Files.Should().OnlyContain(f => f.Sha256.StartsWith("sha256:"));
        record.GameBuild.Should().Be("1.0.3725");
        record.ModIds.Should().Contain("scripthookv");
    }

    [Fact]
    public async Task The_whole_run_rolls_back_to_the_starting_state()
    {
        // The property that matters: after a rollback the game folder is exactly
        // what it was before the run.
        File.WriteAllText(Path.Combine(_game, "existing.txt"), "was here");

        var shv = Stage("scripthookv", ("dinput8.dll", "a"), ("ScriptHookV.dll", "b"));
        await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", [shv]);

        InGame("dinput8.dll").Should().BeTrue();

        await _runner.RollbackAsync("run-1", _game);

        InGame("dinput8.dll").Should().BeFalse("a newly placed file is removed on rollback");
        InGame("ScriptHookV.dll").Should().BeFalse();
        ReadGame("existing.txt").Should().Be("was here", "untouched files are left alone");
    }

    [Fact]
    public async Task Mods_are_placed_in_catalogue_order()
    {
        // Core before the plugins. The record's mod list reflects the order.
        var lspdfr = Stage("lspdfr", ("LSPD First Response.dll", "core"));
        var stp = Stage("stoptheped", ("plugins/StopThePed.dll", "plugin"));

        // Pass them out of order; the runner sorts by catalogue Order.
        var record = await _runner.RunAsync("run-1", _game, "full-duty", "1.0.3725", [stp, lspdfr]);

        var order = record.ModIds.ToList();
        order.IndexOf("lspdfr").Should().BeLessThan(order.IndexOf("stoptheped"));
    }

    [Fact]
    public async Task The_record_is_written_to_disk()
    {
        var shv = Stage("scripthookv", ("dinput8.dll", "a"));

        await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", [shv]);

        File.Exists(_paths.InstallRecordFile).Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_run_produces_an_empty_record_rather_than_throwing()
    {
        var record = await _runner.RunAsync("run-1", _game, "standard", "1.0.3725", []);

        record.Files.Should().BeEmpty();
        record.IsInstalled.Should().BeFalse();
    }
}
