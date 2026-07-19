using Dispatch.Core.Infrastructure;
using Dispatch.Core.Installation;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Installation;

/// <summary>
/// Placement is the one thing that writes into a game folder, so the property
/// that matters most is that every write is reversible: place a set of files,
/// roll the run back, and the folder is byte-for-byte what it was before. This
/// runs entirely against a temp fixture — no real game is ever touched.
/// </summary>
public sealed class FilePlacerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-place", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly string _staging;
    private readonly string _journalPath;
    private readonly BackupStore _backups;
    private readonly FilePlacer _placer;
    private readonly Rollback _rollback;

    public FilePlacerTests()
    {
        _game = Path.Combine(_root, "game");
        _staging = Path.Combine(_root, "staging");
        _journalPath = Path.Combine(_root, "run.jsonl");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_staging);

        _backups = new BackupStore(Path.Combine(_root, "backups"), NullLogger<BackupStore>.Instance);
        _placer = new FilePlacer(_backups, NullLogger<FilePlacer>.Instance);
        _rollback = new Rollback(_backups, NullLogger<Rollback>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string Staged(string name, string content)
    {
        var path = Path.Combine(_staging, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string GamePathOf(string relative) =>
        Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));

    private void GivenInGame(string relative, string content)
    {
        var full = GamePathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ===== Placing ========================================================

    [Fact]
    public async Task Placing_a_new_file_puts_it_in_the_game_folder()
    {
        var staged = Staged("thing.dll", "payload");

        await using var journal = RunJournal.Create(_journalPath, "run-1");
        var outcome = await _placer.PlaceAsync("run-1", _game, staged, "plugins/thing.dll", "mod", journal);

        outcome.Should().Be(PlacementOutcome.Placed);
        File.ReadAllText(GamePathOf("plugins/thing.dll")).Should().Be("payload");
    }

    [Fact]
    public async Task Placing_over_an_existing_file_reports_replaced_and_backs_it_up()
    {
        GivenInGame("config.ini", "old");
        var staged = Staged("config.ini", "new");

        await using var journal = RunJournal.Create(_journalPath, "run-1");
        var outcome = await _placer.PlaceAsync("run-1", _game, staged, "config.ini", "mod", journal);

        outcome.Should().Be(PlacementOutcome.Replaced);
        File.ReadAllText(GamePathOf("config.ini")).Should().Be("new");
    }

    [Fact]
    public async Task A_protected_assembly_that_already_exists_is_left_untouched()
    {
        // Hard rule one: overwriting Callout Interface's assembly silently
        // breaks it.
        GivenInGame("plugins/CalloutInterface.ApplicationExtension.dll", "the real one");
        var staged = Staged("copy.dll", "Grammar Police's stale copy");

        await using var journal = RunJournal.Create(_journalPath, "run-1");
        var outcome = await _placer.PlaceAsync(
            "run-1", _game, staged, "plugins/CalloutInterface.ApplicationExtension.dll", "grammarpolice", journal);

        outcome.Should().Be(PlacementOutcome.SkippedProtected);
        File.ReadAllText(GamePathOf("plugins/CalloutInterface.ApplicationExtension.dll"))
            .Should().Be("the real one", "the protected assembly is never overwritten");
    }

    [Fact]
    public async Task A_read_only_target_is_replaced()
    {
        GivenInGame("locked.ini", "old");
        var target = GamePathOf("locked.ini");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var staged = Staged("locked.ini", "new");

        try
        {
            await using var journal = RunJournal.Create(_journalPath, "run-1");
            await _placer.PlaceAsync("run-1", _game, staged, "locked.ini", "mod", journal);

            File.ReadAllText(target).Should().Be("new");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task A_completed_placement_is_journalled_with_its_hash()
    {
        var staged = Staged("thing.dll", "payload");

        await using (var journal = RunJournal.Create(_journalPath, "run-1"))
        {
            await _placer.PlaceAsync("run-1", _game, staged, "thing.dll", "mod", journal);
        }

        var entries = await RunJournal.ReadAsync(_journalPath);
        entries.Should().ContainSingle();
        entries[0].State.Should().Be(JournalState.Complete);
        entries[0].Hash.Should().Be(await Hashing.Sha256Async(GamePathOf("thing.dll")));
    }

    // ===== The reversibility round trip ===================================

    [Fact]
    public async Task Rolling_back_removes_newly_placed_files()
    {
        var staged = Staged("new.dll", "x");

        await using (var journal = RunJournal.Create(_journalPath, "run-1"))
        {
            await _placer.PlaceAsync("run-1", _game, staged, "plugins/new.dll", "mod", journal);
        }

        var failures = await _rollback.RunAsync(_journalPath, _game);

        failures.Should().BeEmpty();
        File.Exists(GamePathOf("plugins/new.dll")).Should().BeFalse("a newly placed file is removed on rollback");
    }

    [Fact]
    public async Task Rolling_back_restores_replaced_files_byte_for_byte()
    {
        GivenInGame("config.ini", "the original config");
        var originalHash = await Hashing.Sha256Async(GamePathOf("config.ini"));
        var staged = Staged("config.ini", "the install's version");

        await using (var journal = RunJournal.Create(_journalPath, "run-1"))
        {
            await _placer.PlaceAsync("run-1", _game, staged, "config.ini", "mod", journal);
        }

        await _rollback.RunAsync(_journalPath, _game);

        (await Hashing.Sha256Async(GamePathOf("config.ini"))).Should().Be(originalHash);
        File.ReadAllText(GamePathOf("config.ini")).Should().Be("the original config");
    }

    [Fact]
    public async Task A_whole_run_rolls_back_to_the_exact_starting_state()
    {
        // The property the entire resilience design exists for: after a rollback
        // the folder is what it was before the run, whether files were new or
        // replaced.
        GivenInGame("existing.ini", "was here");
        var existingHash = await Hashing.Sha256Async(GamePathOf("existing.ini"));

        await using (var journal = RunJournal.Create(_journalPath, "run-1"))
        {
            await _placer.PlaceAsync("run-1", _game, Staged("a.dll", "A"), "plugins/a.dll", "m1", journal);
            await _placer.PlaceAsync("run-1", _game, Staged("b.dll", "B"), "scripts/b.dll", "m2", journal);
            await _placer.PlaceAsync("run-1", _game, Staged("existing.ini", "changed"), "existing.ini", "m3", journal);
        }

        await _rollback.RunAsync(_journalPath, _game);

        File.Exists(GamePathOf("plugins/a.dll")).Should().BeFalse();
        File.Exists(GamePathOf("scripts/b.dll")).Should().BeFalse();
        (await Hashing.Sha256Async(GamePathOf("existing.ini"))).Should().Be(existingHash);
    }

    [Fact]
    public async Task Rollback_walks_backwards_so_a_double_replace_restores_correctly()
    {
        // Two placements hit the same file. Undone in reverse, the original
        // comes back; undone forward, an intermediate would linger.
        GivenInGame("shared.dll", "v0 original");

        await using (var journal = RunJournal.Create(_journalPath, "run-1"))
        {
            await _placer.PlaceAsync("run-1", _game, Staged("s1.dll", "v1"), "shared.dll", "m1", journal);
            await _placer.PlaceAsync("run-1", _game, Staged("s2.dll", "v2"), "shared.dll", "m2", journal);
        }

        await _rollback.RunAsync(_journalPath, _game);

        File.ReadAllText(GamePathOf("shared.dll")).Should().Be("v0 original");
    }
}
