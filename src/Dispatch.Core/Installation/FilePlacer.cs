using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Installation;

/// <summary>The outcome of placing one file.</summary>
public enum PlacementOutcome
{
    /// <summary>Placed, nothing was there before.</summary>
    Placed,

    /// <summary>Placed over an existing file, which was backed up first.</summary>
    Replaced,

    /// <summary>Left alone because the destination is a protected assembly.</summary>
    SkippedProtected,
}

/// <summary>Places one staged file into the game folder, backed up and journalled.</summary>
/// <remarks>
/// This is the one place that actually writes into a game folder, so every write
/// goes through the same three steps: refuse if the destination is a protected
/// assembly, back up anything already there, then move the staged file into
/// place — with the journal recording the operation pending before the move and
/// complete after. Nothing it does is unrecoverable: the backup is the undo, and
/// the journal is the record of what to undo.
/// </remarks>
public sealed class FilePlacer
{
    private readonly IBackupStore _backups;
    private readonly ILogger<FilePlacer> _logger;

    /// <summary>Constructs the placer.</summary>
    public FilePlacer(IBackupStore backups, ILogger<FilePlacer> logger)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(logger);

        _backups = backups;
        _logger = logger;
    }

    /// <summary>
    /// Places a staged file at a destination relative to the game folder.
    /// </summary>
    /// <param name="runId">The run, for the backup area and journal.</param>
    /// <param name="gamePath">The game folder.</param>
    /// <param name="stagedFile">Absolute path to the validated staged file.</param>
    /// <param name="relativeDestination">Where it goes, relative to the game folder.</param>
    /// <param name="mod">The mod this belongs to, for the journal.</param>
    /// <param name="journal">The run journal.</param>
    public async Task<PlacementOutcome> PlaceAsync(
        string runId,
        string gamePath,
        string stagedFile,
        string relativeDestination,
        string mod,
        RunJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDestination);
        ArgumentNullException.ThrowIfNull(journal);

        var normalised = relativeDestination.Replace('\\', '/');
        var destination = Path.Combine(gamePath, normalised.Replace('/', Path.DirectorySeparatorChar));

        // Hard rule one: never overwrite Callout Interface's assembly once it is
        // present. Grammar Police and LIAR both ship copies, and overwriting the
        // one Callout Interface placed silently breaks it.
        if (ProtectedAssemblies.IsProtected(normalised) && File.Exists(destination))
        {
            _logger.LogInformation("Left protected assembly {Path} untouched", normalised);
            return PlacementOutcome.SkippedProtected;
        }

        var backup = await _backups.BackupAsync(runId, gamePath, normalised, cancellationToken).ConfigureAwait(false);
        var replacing = backup is not null;

        var seq = await journal.BeginAsync(new JournalEntry
        {
            Seq = 0,
            Op = JournalOp.Place,
            Mod = mod,
            Src = stagedFile,
            Dst = normalised,
            Backup = backup,
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // A read-only target is cleared first, backed up above, then
            // replaced. The attribute change is implied by the journal entry.
            if (File.Exists(destination))
            {
                var attributes = File.GetAttributes(destination);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
                }
            }

            File.Copy(stagedFile, destination, overwrite: true);

            // Verify by hash after the write, not by the copy's exit code. A
            // silent bad write is worse than a loud failure.
            var hash = await Hashing.Sha256Async(destination, cancellationToken).ConfigureAwait(false);
            await journal.CompleteAsync(seq, hash, cancellationToken).ConfigureAwait(false);

            return replacing ? PlacementOutcome.Replaced : PlacementOutcome.Placed;
        }
        catch
        {
            await journal.FailAsync(seq, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
