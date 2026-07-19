using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Maintenance;

/// <summary>
/// Backups are what make an install reversible: every overwrite is copied first,
/// so a crash mid-run or a rollback puts the original back byte-identical. These
/// tests care that the copy happens before the overwrite, that a restore is
/// verified, and that placing a new file (nothing to back up) is handled
/// distinctly from replacing one.
/// </summary>
public sealed class BackupStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-backup", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly BackupStore _store;

    public BackupStoreTests()
    {
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(_game);
        _store = new BackupStore(Path.Combine(_root, "backups"), NullLogger<BackupStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string Given(string relativePath, string content)
    {
        var full = Path.Combine(_game, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public async Task Backing_up_an_existing_file_returns_a_backup_path()
    {
        Given("plugins/StopThePed.dll", "version 8.4");

        var backup = await _store.BackupAsync("run-1", _game, "plugins/StopThePed.dll");

        backup.Should().NotBeNull();
        File.Exists(backup).Should().BeTrue();
        (await File.ReadAllTextAsync(backup!)).Should().Be("version 8.4");
    }

    [Fact]
    public async Task Backing_up_leaves_the_original_in_place()
    {
        // The original must survive until the installer actually overwrites it,
        // or a crash between backup and write loses the file entirely.
        var original = Given("thing.dll", "original");

        await _store.BackupAsync("run-1", _game, "thing.dll");

        File.Exists(original).Should().BeTrue("backup copies rather than moves");
        (await File.ReadAllTextAsync(original)).Should().Be("original");
    }

    [Fact]
    public async Task Backing_up_a_file_that_does_not_exist_returns_null()
    {
        // The installer is placing a new file, not replacing one; a rollback
        // removes it rather than restoring anything.
        var backup = await _store.BackupAsync("run-1", _game, "brand-new.dll");

        backup.Should().BeNull();
    }

    [Fact]
    public async Task Restoring_puts_the_backed_up_bytes_back()
    {
        Given("config.ini", "original config");
        var backup = await _store.BackupAsync("run-1", _game, "config.ini");

        // Simulate the installer overwriting it.
        Given("config.ini", "overwritten by install");

        await _store.RestoreAsync(backup!, _game, "config.ini");

        (await File.ReadAllTextAsync(Path.Combine(_game, "config.ini"))).Should().Be("original config");
    }

    [Fact]
    public async Task A_restore_is_byte_identical_to_what_was_backed_up()
    {
        var original = Given("ScriptHookV.dll", "the quick brown fox");
        var beforeHash = await Hashing.Sha256Async(original);
        var backup = await _store.BackupAsync("run-1", _game, "ScriptHookV.dll");

        File.WriteAllText(original, "corrupted");
        await _store.RestoreAsync(backup!, _game, "ScriptHookV.dll");

        (await Hashing.Sha256Async(original)).Should().Be(beforeHash);
    }

    [Fact]
    public async Task The_backup_mirrors_the_game_folder_structure()
    {
        // A rollback walks the tree and restores by path, so the structure is
        // the record.
        Given("plugins/lspdfr/deep/Thing.dll", "x");

        var backup = await _store.BackupAsync("run-7f3a", _game, "plugins/lspdfr/deep/Thing.dll");

        backup!.Replace('\\', '/').Should().EndWith("run-7f3a/plugins/lspdfr/deep/Thing.dll");
    }

    [Fact]
    public async Task Restoring_a_missing_backup_throws()
    {
        var act = async () => await _store.RestoreAsync(
            Path.Combine(_root, "nope.dll"), _game, "nope.dll");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Two_runs_keep_separate_backups()
    {
        Given("thing.dll", "run one sees this");
        var first = await _store.BackupAsync("run-1", _game, "thing.dll");

        File.WriteAllText(Path.Combine(_game, "thing.dll"), "run two sees this");
        var second = await _store.BackupAsync("run-2", _game, "thing.dll");

        (await File.ReadAllTextAsync(first!)).Should().Be("run one sees this");
        (await File.ReadAllTextAsync(second!)).Should().Be("run two sees this");
    }

    [Fact]
    public async Task Pruning_keeps_only_the_most_recent_runs()
    {
        // Backup runs accumulate; the spec keeps the last ten.
        for (var i = 0; i < 5; i++)
        {
            Given($"file{i}.dll", "x");
            await _store.BackupAsync($"run-{i}", _game, $"file{i}.dll");
            await Task.Delay(20);
        }

        _store.Prune(keep: 2);

        var remaining = Directory.GetDirectories(Path.Combine(_root, "backups"));
        remaining.Should().HaveCount(2);
    }
}
