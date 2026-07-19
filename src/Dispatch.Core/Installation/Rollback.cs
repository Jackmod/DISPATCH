using Dispatch.Core.Maintenance;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Installation;

/// <summary>
/// Undoes a run by walking its journal backwards.
/// </summary>
/// <remarks>
/// This is the other half of what the journal buys: for every completed
/// placement, restore the backup if there was one, or remove the file if it was
/// newly placed. Walking backwards matters — a later operation may have replaced
/// a file an earlier one placed, so undoing in reverse restores the correct
/// intermediate state at each step rather than a scrambled one.
///
/// <para>
/// A rollback that itself fails partway must not throw away what it can still
/// undo, so each entry is handled independently and failures are collected and
/// returned rather than aborting the whole reversal.
/// </para>
/// </remarks>
public sealed class Rollback
{
    private readonly IBackupStore _backups;
    private readonly ILogger<Rollback> _logger;

    /// <summary>Constructs the rollback.</summary>
    public Rollback(IBackupStore backups, ILogger<Rollback> logger)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(logger);

        _backups = backups;
        _logger = logger;
    }

    /// <summary>
    /// Rolls a run back to the state before it started, from its journal.
    /// </summary>
    /// <returns>Files that could not be reverted, each with a reason. Empty on a clean rollback.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(
        string journalPath,
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var entries = await RunJournal.ReadAsync(journalPath, cancellationToken).ConfigureAwait(false);
        var failures = new List<string>();

        // Backwards: undo the most recent operation first.
        foreach (var entry in entries.Where(e => e.Op == JournalOp.Place).Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Dst is null)
            {
                continue;
            }

            var destination = Path.Combine(gamePath, entry.Dst.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (entry.Backup is not null)
                {
                    // Something was there before; put it back.
                    await _backups.RestoreAsync(entry.Backup, gamePath, entry.Dst, cancellationToken).ConfigureAwait(false);
                }
                else if (File.Exists(destination))
                {
                    // Newly placed by this run; remove it.
                    File.Delete(destination);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                // Collect and continue: a rollback that aborts on the first
                // problem leaves the folder in a worse state than one that
                // reverts everything it can.
                _logger.LogWarning(ex, "Could not roll back {Path}", entry.Dst);
                failures.Add($"{entry.Dst}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Rolled back {Total} placement(s), {Failed} could not be reverted",
            entries.Count(e => e.Op == JournalOp.Place), failures.Count);

        return failures;
    }
}
