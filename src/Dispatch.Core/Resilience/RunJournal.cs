using System.Text;
using System.Text.Json;

namespace Dispatch.Core.Resilience;

/// <summary>
/// The append-only record of everything a run does, flushed after every line so
/// it survives the crash that makes it necessary.
/// </summary>
/// <remarks>
/// Newline-delimited JSON, one object per line, at
/// <c>%LOCALAPPDATA%/Dispatch/runs/&lt;run-id&gt;.jsonl</c>. Each operation is
/// written <see cref="JournalState.Pending"/> before it runs and updated to
/// <see cref="JournalState.Complete"/> after, which gives resume and rollback
/// for free: replay forwards skipping completed entries, or walk backwards
/// restoring backups.
///
/// <para>
/// The two properties that matter most are durability and tolerance of its own
/// corruption. Every write is flushed to the OS and <c>fsync</c>'d, so a power
/// cut loses at most the line being written. And a truncated final line — the
/// signature of a crash mid-write — is discarded on read rather than making the
/// whole journal unparseable, because the journal that cannot be read after a
/// crash is worse than no journal at all.
/// </para>
/// </remarks>
public sealed class RunJournal : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _seq;

    private RunJournal(FileStream stream, int lastSeq)
    {
        _stream = stream;
        _seq = lastSeq;
    }

    /// <summary>The run this journal belongs to.</summary>
    public string RunId { get; private init; } = string.Empty;

    /// <summary>Opens a fresh journal for a new run, creating the file.</summary>
    public static RunJournal Create(string path, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new RunJournal(stream, lastSeq: 0) { RunId = runId };
    }

    /// <summary>Opens an existing journal for appending, continuing its sequence.</summary>
    public static async Task<RunJournal> OpenAsync(string path, string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var existing = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var lastSeq = existing.Count > 0 ? existing.Max(e => e.Seq) : 0;

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new RunJournal(stream, lastSeq) { RunId = runId };
    }

    /// <summary>
    /// Writes a pending entry, returning its sequence number. Call before the
    /// operation actually runs.
    /// </summary>
    public async Task<int> BeginAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var seq = ++_seq;
            await WriteLineAsync(entry with { Seq = seq, State = JournalState.Pending }, cancellationToken)
                .ConfigureAwait(false);
            return seq;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Marks an operation complete, recording the placed file's hash.</summary>
    public Task CompleteAsync(int seq, string? hash = null, CancellationToken cancellationToken = default) =>
        AppendStateAsync(new JournalEntry { Seq = seq, State = JournalState.Complete, Hash = hash }, cancellationToken);

    /// <summary>Marks an operation failed.</summary>
    public Task FailAsync(int seq, CancellationToken cancellationToken = default) =>
        AppendStateAsync(new JournalEntry { Seq = seq, State = JournalState.Failed }, cancellationToken);

    private async Task AppendStateAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteLineAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteLineAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entry, Json);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Flush to the physical disk, not just the OS buffer. Without this a
        // power cut can lose entries the app believes are safely journalled,
        // and the recovery is only as good as what actually reached the platter.
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Reads a journal back into a coherent picture of the run: the latest state
    /// of each operation, tolerating a truncated final line.
    /// </summary>
    public static async Task<IReadOnlyList<JournalEntry>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        // Later lines carry state updates for an earlier seq, so fold them
        // together: the last state written for a seq wins, but the operation's
        // details come from its original pending line.
        var byseq = new Dictionary<int, JournalEntry>();

        // Shared ReadWrite, so the journal can be read while the run that owns
        // it still holds it open for appending — which is exactly the case a
        // live progress view or a mid-run inspection needs.
        var lines = await ReadAllLinesSharedAsync(path, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JournalEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<JournalEntry>(line, Json);
            }
            catch (JsonException)
            {
                // A truncated final line is the fingerprint of a crash mid-write.
                // Discard it and keep everything before it, rather than treating
                // the whole journal as lost.
                if (i == lines.Length - 1)
                {
                    break;
                }

                // A broken line in the middle means real corruption; skip it but
                // keep going, since the surrounding entries are still usable.
                continue;
            }

            if (entry is null)
            {
                continue;
            }

            if (byseq.TryGetValue(entry.Seq, out var original))
            {
                // A state-update line: keep the original operation details,
                // apply the newer state and hash.
                byseq[entry.Seq] = original with
                {
                    State = entry.State,
                    Hash = entry.Hash ?? original.Hash,
                };
            }
            else
            {
                byseq[entry.Seq] = entry;
            }
        }

        return byseq.Values.OrderBy(e => e.Seq).ToList();
    }

    private static async Task<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }

    /// <summary>The operations that were left incomplete, in order — what a resume continues from.</summary>
    public static async Task<IReadOnlyList<JournalEntry>> IncompleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var all = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return all.Where(e => e.State != JournalState.Complete).ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
        _gate.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
