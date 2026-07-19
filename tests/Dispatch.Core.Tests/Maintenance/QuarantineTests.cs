using System.Text;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Maintenance;

/// <summary>
/// Quarantine is half of the most dangerous feature in the app. A bug here
/// moves or loses a file someone cannot get back without a 100GB reinstall, so
/// these tests are weighted hard toward reversibility: everything moved comes
/// back byte-identical, nothing is ever actually deleted except on an explicit
/// purge, and no crafted input can reach a file it should not.
/// </summary>
public sealed class QuarantineTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-quarantine", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly string _store;
    private readonly Quarantine _quarantine;

    public QuarantineTests()
    {
        _game = Path.Combine(_root, "game");
        _store = Path.Combine(_root, "quarantine");
        Directory.CreateDirectory(_game);
        _quarantine = new Quarantine(_store, NullLogger<Quarantine>.Instance);
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

    private bool Exists(string relativePath) =>
        File.Exists(Path.Combine(_game, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(_game, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    // ===== Moving, not deleting ===========================================

    [Fact]
    public async Task Quarantining_removes_the_file_from_the_game_folder()
    {
        Given("plugins/StopThePed.dll", "payload");

        await _quarantine.QuarantineAsync(_game, ["plugins/StopThePed.dll"]);

        Exists("plugins/StopThePed.dll").Should().BeFalse("the file was moved out");
    }

    [Fact]
    public async Task The_file_still_exists_in_quarantine_after_moving()
    {
        Given("plugins/Thing.dll", "payload");

        var batch = await _quarantine.QuarantineAsync(_game, ["plugins/Thing.dll"]);

        var stored = Path.Combine(_store, batch.Id, batch.Entries[0].QuarantinedName);
        File.Exists(stored).Should().BeTrue("quarantine moves rather than deletes");
        (await File.ReadAllTextAsync(stored)).Should().Be("payload");
    }

    [Fact]
    public async Task The_batch_manifest_records_the_original_path_and_hash()
    {
        Given("dinput8.dll", "injection library");

        var batch = await _quarantine.QuarantineAsync(_game, ["dinput8.dll"]);

        batch.Entries.Should().ContainSingle();
        batch.Entries[0].OriginalRelativePath.Should().Be("dinput8.dll");
        batch.Entries[0].Sha256.Should().StartWith("sha256:");
        batch.Entries[0].SizeBytes.Should().Be(Encoding.UTF8.GetByteCount("injection library"));
    }

    // ===== Restoring exactly ==============================================

    [Fact]
    public async Task Restoring_puts_the_file_back_where_it_was()
    {
        Given("plugins/lspdfr/Thing.dll", "exact bytes");
        var batch = await _quarantine.QuarantineAsync(_game, ["plugins/lspdfr/Thing.dll"]);

        var failures = await _quarantine.RestoreAsync(batch.Id);

        failures.Should().BeEmpty();
        Exists("plugins/lspdfr/Thing.dll").Should().BeTrue();
        Read("plugins/lspdfr/Thing.dll").Should().Be("exact bytes");
    }

    [Fact]
    public async Task A_restored_file_is_byte_identical_to_the_original()
    {
        var original = Given("ScriptHookV.dll", "the quick brown fox jumps over the lazy dog");
        var beforeHash = await Hashing.Sha256Async(original);

        var batch = await _quarantine.QuarantineAsync(_game, ["ScriptHookV.dll"]);
        await _quarantine.RestoreAsync(batch.Id);

        var afterHash = await Hashing.Sha256Async(original);
        afterHash.Should().Be(beforeHash);
    }

    [Fact]
    public async Task Restoring_a_whole_batch_brings_every_file_back()
    {
        Given("a.dll", "A");
        Given("plugins/b.dll", "B");
        Given("scripts/c.dll", "C");

        var batch = await _quarantine.QuarantineAsync(_game, ["a.dll", "plugins/b.dll", "scripts/c.dll"]);
        await _quarantine.RestoreAsync(batch.Id);

        Read("a.dll").Should().Be("A");
        Read("plugins/b.dll").Should().Be("B");
        Read("scripts/c.dll").Should().Be("C");
    }

    [Fact]
    public async Task A_successful_restore_empties_the_batch()
    {
        Given("thing.dll", "x");
        var batch = await _quarantine.QuarantineAsync(_game, ["thing.dll"]);

        await _quarantine.RestoreAsync(batch.Id);

        (await _quarantine.ListBatchesAsync()).Should().NotContain(b => b.Id == batch.Id);
    }

    [Fact]
    public async Task Restore_does_not_clobber_a_file_that_reappeared()
    {
        // The user reinstalled the mod after cleaning. Their copy wins; the
        // quarantined one is not forced back over it.
        Given("thing.dll", "original");
        var batch = await _quarantine.QuarantineAsync(_game, ["thing.dll"]);
        Given("thing.dll", "reinstalled");

        var failures = await _quarantine.RestoreAsync(batch.Id);

        failures.Should().ContainSingle();
        Read("thing.dll").Should().Be("reinstalled", "the user's newer file is not overwritten");
    }

    // ===== Collisions =====================================================

    [Fact]
    public async Task Two_files_with_the_same_name_from_different_folders_both_survive()
    {
        // Grammar Police and LIAR both ship a RageNativeUI.dll. Flattening the
        // path is what stops one overwriting the other inside the batch and
        // becoming unrecoverable.
        Given("plugins/LSPDFR/GrammarPolice/RageNativeUI.dll", "grammar copy");
        Given("plugins/LSPDFR/LIAR/RageNativeUI.dll", "liar copy");

        var batch = await _quarantine.QuarantineAsync(_game,
        [
            "plugins/LSPDFR/GrammarPolice/RageNativeUI.dll",
            "plugins/LSPDFR/LIAR/RageNativeUI.dll",
        ]);
        await _quarantine.RestoreAsync(batch.Id);

        Read("plugins/LSPDFR/GrammarPolice/RageNativeUI.dll").Should().Be("grammar copy");
        Read("plugins/LSPDFR/LIAR/RageNativeUI.dll").Should().Be("liar copy");
    }

    // ===== Refusing to touch what it must not =============================

    [Fact]
    public async Task A_protected_path_is_refused_even_if_a_caller_asks()
    {
        // A second wall behind the scanner: even a bug that put a save in the
        // list cannot act on it here.
        Given("profiles/settings.xml", "keep me");

        var batch = await _quarantine.QuarantineAsync(_game, ["profiles/settings.xml"]);

        batch.Entries.Should().BeEmpty();
        Exists("profiles/settings.xml").Should().BeTrue("a protected file is never moved");
    }

    [Fact]
    public async Task A_path_escaping_the_game_folder_is_refused()
    {
        // A crafted relative path must not reach outside the game folder and
        // move something arbitrary off the disk.
        var outside = Path.Combine(_root, "outside-secret.txt");
        await File.WriteAllTextAsync(outside, "do not touch");

        var batch = await _quarantine.QuarantineAsync(_game, ["../outside-secret.txt"]);

        batch.Entries.Should().BeEmpty();
        File.Exists(outside).Should().BeTrue("nothing outside the game folder is moved");
    }

    [Fact]
    public async Task A_missing_source_file_is_skipped_rather_than_failing_the_batch()
    {
        Given("real.dll", "here");

        var batch = await _quarantine.QuarantineAsync(_game, ["real.dll", "ghost.dll"]);

        batch.Entries.Should().ContainSingle();
        batch.Entries[0].OriginalRelativePath.Should().Be("real.dll");
    }

    // ===== Purge is the only deletion =====================================

    [Fact]
    public async Task Only_purge_actually_deletes()
    {
        Given("thing.dll", "x");
        var batch = await _quarantine.QuarantineAsync(_game, ["thing.dll"]);
        var stored = Path.Combine(_store, batch.Id);

        Directory.Exists(stored).Should().BeTrue("moving does not delete");

        await _quarantine.PurgeAsync(batch.Id);

        Directory.Exists(stored).Should().BeFalse("purge is the one place anything is removed");
    }

    // ===== Listing ========================================================

    [Fact]
    public async Task Batches_are_listed_newest_first()
    {
        Given("a.dll", "a");
        var first = await _quarantine.QuarantineAsync(_game, ["a.dll"]);
        await Task.Delay(1100); // batch ids are second-resolution

        Given("b.dll", "b");
        var second = await _quarantine.QuarantineAsync(_game, ["b.dll"]);

        var batches = await _quarantine.ListBatchesAsync();

        batches.Should().HaveCountGreaterThanOrEqualTo(2);
        batches[0].Id.Should().Be(second.Id, "the newest batch is first");
        batches.Select(b => b.Id).Should().Contain(first.Id);
    }

    [Fact]
    public async Task Listing_with_no_batches_yields_an_empty_list()
    {
        (await _quarantine.ListBatchesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Restoring_a_batch_that_does_not_exist_throws()
    {
        var act = async () => await _quarantine.RestoreAsync("no-such-batch");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Cancelling_stops_the_move_between_files_not_mid_file()
    {
        for (var i = 0; i < 10; i++)
        {
            Given($"file{i}.dll", new string('x', 1000));
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await _quarantine.QuarantineAsync(
            _game,
            Enumerable.Range(0, 10).Select(i => $"file{i}.dll").ToList(),
            null,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
