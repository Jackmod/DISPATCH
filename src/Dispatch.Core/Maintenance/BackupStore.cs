using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Maintenance;

/// <summary>Backs up a file before it is overwritten, so the write is reversible.</summary>
public interface IBackupStore
{
    /// <summary>
    /// Copies a file that is about to be overwritten into the run's backup
    /// area, returning the backup path — or null when there was nothing there
    /// to back up.
    /// </summary>
    /// <param name="runId">The install run this backup belongs to.</param>
    /// <param name="gamePath">The game folder.</param>
    /// <param name="relativePath">The file about to be replaced, relative to the game folder.</param>
    Task<string?> BackupAsync(
        string runId,
        string gamePath,
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a single backed-up file over the current one, verifying it
    /// lands byte-identical to what was backed up.
    /// </summary>
    Task RestoreAsync(string backupPath, string gamePath, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Prunes backup runs beyond the most recent <paramref name="keep"/>.</summary>
    void Prune(int keep = 10);
}

/// <summary>
/// Per-run file backups, so any overwrite an install performs can be undone.
/// </summary>
/// <remarks>
/// The journal records that a backup was taken; this is where the bytes
/// actually live, under <c>%LOCALAPPDATA%/Dispatch/backups/&lt;run-id&gt;/</c>
/// mirroring the game folder's structure. Mirroring rather than flattening
/// means a rollback can walk the tree and put everything back at the same
/// relative path without consulting a manifest — the path <em>is</em> the
/// record.
///
/// <para>
/// A backup is a copy, taken before the overwrite. If the install then crashes
/// mid-write, the original is still intact in the backup area and the journal
/// knows where. That is the whole reason nothing an install does is
/// unrecoverable.
/// </para>
/// </remarks>
public sealed class BackupStore : IBackupStore
{
    private readonly string _root;
    private readonly ILogger<BackupStore> _logger;

    /// <summary>Constructs the store over its backup root.</summary>
    public BackupStore(string backupRoot, ILogger<BackupStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentNullException.ThrowIfNull(logger);

        _root = backupRoot;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> BackupAsync(
        string runId,
        string gamePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalised = relativePath.Replace('\\', '/');
        var source = Path.Combine(gamePath, normalised.Replace('/', Path.DirectorySeparatorChar));

        // Nothing to back up if the file does not exist yet — the install is
        // placing a new file, not replacing one, and a rollback simply removes
        // it rather than restoring anything.
        if (!File.Exists(source))
        {
            return null;
        }

        var mirrorPath = Path.Combine(_root, runId, normalised.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mirrorPath)!);

        // The mirror path is disambiguated if it is already taken. A single run
        // can back up the same file more than once — Search Items Reborn and
        // Ultimate Backup deliberately replace Stop The Ped files that an
        // earlier mod placed — and a second backup landing on the first would
        // overwrite it, losing the original and making a full rollback restore
        // the wrong version. Each distinct path is recorded in the journal, so
        // a rollback still restores exactly the right one.
        var backupPath = mirrorPath;
        var suffix = 2;
        while (File.Exists(backupPath))
        {
            backupPath = $"{mirrorPath}.{suffix++}";
        }

        // Copy, not move: the original has to stay in place until the moment the
        // installer actually overwrites it, or a crash between backup and write
        // leaves the game folder missing the file entirely. On a background
        // thread because a backed-up file can be hundreds of megabytes.
        await Task.Run(() => File.Copy(source, backupPath, overwrite: false), cancellationToken).ConfigureAwait(false);

        // File.Copy carries the read-only attribute across. A read-only backup
        // is an internal artifact this store has to be able to prune and
        // restore over, so the attribute is cleared on the copy.
        var attributes = File.GetAttributes(backupPath);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(backupPath, attributes & ~FileAttributes.ReadOnly);
        }

        _logger.LogDebug("Backed up {Path} for run {Run}", normalised, runId);
        return backupPath;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(
        string backupPath,
        string gamePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup is missing: {backupPath}");
        }

        var expected = await Hashing.Sha256Async(backupPath, cancellationToken).ConfigureAwait(false);

        var destination = Path.Combine(gamePath, relativePath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        File.Copy(backupPath, destination, overwrite: true);

        // A rollback that silently corrupted the file it was restoring would be
        // the worst possible outcome, so it is verified.
        var actual = await Hashing.Sha256Async(destination, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new IOException($"Restore of {relativePath} did not match the backup.");
        }

        _logger.LogDebug("Restored {Path} from backup", relativePath);
    }

    /// <inheritdoc />
    public void Prune(int keep = 10)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        var runs = Directory.GetDirectories(_root)
            .Select(dir => new DirectoryInfo(dir))
            .OrderByDescending(info => info.CreationTimeUtc)
            .Skip(keep)
            .ToList();

        foreach (var run in runs)
        {
            try
            {
                run.Delete(recursive: true);
                _logger.LogInformation("Pruned old backup run {Run}", run.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not prune backup run {Run}", run.Name);
            }
        }
    }
}
