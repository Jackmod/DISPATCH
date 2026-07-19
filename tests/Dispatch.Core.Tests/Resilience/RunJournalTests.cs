using Dispatch.Core.Resilience;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Resilience;

/// <summary>
/// The journal is the recovery mechanism, so it has to survive the crash that
/// made it necessary. These tests care most about that: a truncated final line
/// is tolerated, sequence numbers are monotonic and gap-detectable, and the
/// folded read gives a coherent picture of which operations completed.
/// </summary>
public sealed class RunJournalTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "dispatch-journal", Guid.NewGuid().ToString("N"));

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    public RunJournalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static JournalEntry Place(string mod, string dst) =>
        new() { Seq = 0, Op = JournalOp.Place, Mod = mod, Dst = dst };

    [Fact]
    public async Task An_operation_is_written_pending_then_complete()
    {
        var path = Path_("run.jsonl");

        await using (var journal = RunJournal.Create(path, "run-1"))
        {
            var seq = await journal.BeginAsync(Place("stoptheped", "plugins/StopThePed.dll"));
            await journal.CompleteAsync(seq, "sha256:abc");
        }

        var entries = await RunJournal.ReadAsync(path);

        entries.Should().ContainSingle();
        entries[0].State.Should().Be(JournalState.Complete);
        entries[0].Hash.Should().Be("sha256:abc");
        entries[0].Dst.Should().Be("plugins/StopThePed.dll", "the details come from the pending line");
    }

    [Fact]
    public async Task Sequence_numbers_are_monotonic()
    {
        var path = Path_("run.jsonl");

        await using var journal = RunJournal.Create(path, "run-1");
        var a = await journal.BeginAsync(Place("a", "a.dll"));
        var b = await journal.BeginAsync(Place("b", "b.dll"));
        var c = await journal.BeginAsync(Place("c", "c.dll"));

        new[] { a, b, c }.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task An_incomplete_operation_is_reported_for_resume()
    {
        var path = Path_("run.jsonl");

        await using (var journal = RunJournal.Create(path, "run-1"))
        {
            var done = await journal.BeginAsync(Place("done", "done.dll"));
            await journal.CompleteAsync(done);

            // Begun but never completed - the crash happened here.
            await journal.BeginAsync(Place("half", "half.dll"));
        }

        var incomplete = await RunJournal.IncompleteAsync(path);

        incomplete.Should().ContainSingle();
        incomplete[0].Mod.Should().Be("half");
    }

    [Fact]
    public async Task A_truncated_final_line_is_discarded_not_fatal()
    {
        // The fingerprint of a crash mid-write. The journal must still be
        // readable, minus the ambiguous last operation.
        var path = Path_("run.jsonl");

        await using (var journal = RunJournal.Create(path, "run-1"))
        {
            var seq = await journal.BeginAsync(Place("good", "good.dll"));
            await journal.CompleteAsync(seq);
        }

        // Append a half-written line, as a power cut would leave.
        await File.AppendAllTextAsync(path, "{\"seq\":2,\"op\":\"Place\",\"mod\":\"trunc");

        var entries = await RunJournal.ReadAsync(path);

        entries.Should().ContainSingle("the complete operation survives");
        entries[0].Mod.Should().Be("good");
    }

    [Fact]
    public async Task A_completely_absent_journal_reads_as_empty()
    {
        (await RunJournal.ReadAsync(Path_("never-written.jsonl"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Reopening_a_journal_continues_its_sequence()
    {
        // A resume in a later session appends rather than restarting the count,
        // or two operations would share a seq and the fold would merge them.
        var path = Path_("run.jsonl");

        await using (var first = RunJournal.Create(path, "run-1"))
        {
            var seq = await first.BeginAsync(Place("a", "a.dll"));
            await first.CompleteAsync(seq);
        }

        await using (var reopened = await RunJournal.OpenAsync(path, "run-1"))
        {
            var seq = await reopened.BeginAsync(Place("b", "b.dll"));
            await reopened.CompleteAsync(seq);
        }

        var entries = await RunJournal.ReadAsync(path);

        entries.Should().HaveCount(2);
        entries.Select(e => e.Seq).Should().Equal(1, 2);
    }

    [Fact]
    public async Task A_config_write_records_old_and_new_values()
    {
        // Re-running a completed config write must be a no-op, which needs the
        // old and new values on the record.
        var path = Path_("run.jsonl");

        await using var journal = RunJournal.Create(path, "run-1");
        var seq = await journal.BeginAsync(new JournalEntry
        {
            Seq = 0,
            Op = JournalOp.Configure,
            Mod = "stoptheped",
            Dst = "plugins/StopThePed.ini",
            Key = "General/PatDownKey",
            OldValue = "F9",
            NewValue = "F10",
        });
        await journal.CompleteAsync(seq);

        var entries = await RunJournal.ReadAsync(path);
        entries[0].OldValue.Should().Be("F9");
        entries[0].NewValue.Should().Be("F10");
    }

    [Fact]
    public async Task A_failed_operation_is_not_reported_as_complete()
    {
        var path = Path_("run.jsonl");

        await using (var journal = RunJournal.Create(path, "run-1"))
        {
            var seq = await journal.BeginAsync(Place("bad", "bad.dll"));
            await journal.FailAsync(seq);
        }

        var entries = await RunJournal.ReadAsync(path);
        entries[0].State.Should().Be(JournalState.Failed);
        (await RunJournal.IncompleteAsync(path)).Should().ContainSingle();
    }

    [Fact]
    public async Task The_backup_path_is_preserved_for_rollback()
    {
        var path = Path_("run.jsonl");

        await using var journal = RunJournal.Create(path, "run-1");
        var seq = await journal.BeginAsync(Place("m", "plugins/M.dll") with
        {
            Backup = "backups/run-1/plugins/M.dll",
        });
        await journal.CompleteAsync(seq);

        var entries = await RunJournal.ReadAsync(path);
        entries[0].Backup.Should().Be("backups/run-1/plugins/M.dll");
    }
}
