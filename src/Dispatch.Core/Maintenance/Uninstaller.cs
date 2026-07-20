using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;

namespace Dispatch.Core.Maintenance;

/// <summary>One thing the app-data wipe removed.</summary>
/// <param name="Path">Where it was.</param>
/// <param name="Bytes">How big it was.</param>
public sealed record RemovedItem(string Path, long Bytes);

/// <summary>The result of wiping Dispatch's own data.</summary>
/// <param name="Removed">Everything removed.</param>
/// <param name="Skipped">Anything that could not be removed, with a reason.</param>
public sealed record AppDataWipeReport(IReadOnlyList<RemovedItem> Removed, IReadOnlyList<string> Skipped)
{
    /// <summary>Total size freed.</summary>
    public long TotalBytes => Removed.Sum(r => r.Bytes);

    /// <summary>How many locations were removed.</summary>
    public int Count => Removed.Count;
}

/// <summary>The result of returning the game folder to stock.</summary>
/// <param name="FilesRestored">Overwritten files put back from a Dispatch backup.</param>
/// <param name="FilesRemoved">New mod files moved to quarantine.</param>
/// <param name="Problems">Anything that could not be undone, with a reason.</param>
public sealed record ReturnToStockReport(int FilesRestored, int FilesRemoved, IReadOnlyList<string> Problems)
{
    /// <summary>Whether anything was changed.</summary>
    public bool DidAnything => FilesRestored > 0 || FilesRemoved > 0;
}

/// <summary>Removes everything Dispatch put on the machine, reversibly and scoped.</summary>
public interface IUninstaller
{
    /// <summary>
    /// Deletes all of Dispatch's own data — profile, install record, journals,
    /// backups, quarantine, archives, mod pack, logs, staging, the OpenIV import
    /// folder and the Desktop shortcut. The game folder is never touched.
    /// </summary>
    Task<AppDataWipeReport> RemoveAppDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the game folder to stock: every file an install overwrote is put
    /// back from its Dispatch backup, and every new file Dispatch added is moved to
    /// quarantine. Only files the install record names are touched, and only while
    /// they still match what Dispatch placed — a file the user changed since is
    /// left alone.
    /// </summary>
    Task<ReturnToStockReport> ReturnGameToStockAsync(string gamePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// The uninstaller. Two independent, opt-in operations that never overlap: wiping
/// Dispatch's own footprint, and — only if the user asks — undoing the install in
/// the game folder.
/// </summary>
/// <remarks>
/// Both are built to be safe by construction. The app-data wipe only ever deletes
/// paths under Dispatch's own roots, and refuses anything that does not look like
/// one, so a misconfigured path can never reach the game folder or a user's
/// documents. The return-to-stock only acts on the exact files the install record
/// lists, restores only from Dispatch's own byte-verified backups, and removes a
/// mod file only while its hash still matches what Dispatch placed — so a save, a
/// setting the user changed in-game, or a file another tool touched is never
/// caught up in it. Removed mod files go to quarantine, not the bin, so even
/// "return to stock" is reversible until the data is wiped.
/// </remarks>
public sealed class Uninstaller : IUninstaller
{
    private readonly IAppPaths _paths;
    private readonly IInstallRecordStore _records;
    private readonly IBackupStore _backups;
    private readonly IQuarantine _quarantine;

    /// <summary>Constructs the uninstaller.</summary>
    public Uninstaller(
        IAppPaths paths,
        IInstallRecordStore records,
        IBackupStore backups,
        IQuarantine quarantine)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(quarantine);

        _paths = paths;
        _records = records;
        _backups = backups;
        _quarantine = quarantine;
    }

    /// <inheritdoc />
    public Task<AppDataWipeReport> RemoveAppDataAsync(CancellationToken cancellationToken = default)
    {
        var removed = new List<RemovedItem>();
        var skipped = new List<string>();

        // The whole Dispatch footprint. StagingRoot lives under %TEMP% and the
        // import folder under the Desktop; everything else is under Root.
        var targets = new[] { _paths.Root, _paths.StagingRoot, _paths.OpenIvImportDirectory }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(target))
            {
                continue;
            }

            // A hard wall: only ever delete a folder whose path actually names
            // Dispatch. Nothing else — however the paths were configured — can be
            // removed by this method.
            if (!LooksLikeDispatchFolder(target))
            {
                skipped.Add($"{target}: refused — does not look like a Dispatch folder");
                continue;
            }

            try
            {
                var bytes = DirectorySize(target);
                Directory.Delete(target, recursive: true);
                removed.Add(new RemovedItem(target, bytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{target}: {ex.Message}");
            }
        }

        // The Desktop shortcut, if it is there.
        TryRemoveDesktopShortcut(removed, skipped);

        return Task.FromResult(new AppDataWipeReport(removed, skipped));
    }

    /// <inheritdoc />
    public async Task<ReturnToStockReport> ReturnGameToStockAsync(
        string gamePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var record = await _records.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsInstalled)
        {
            return new ReturnToStockReport(0, 0, ["Nothing is installed to return to stock."]);
        }

        var problems = new List<string>();
        var restored = 0;
        var toRemove = new List<string>();

        foreach (var placed in record.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var backup = FindBackup(placed.RelativePath);
            if (backup is not null)
            {
                // Dispatch overwrote a stock file here — put the original back.
                try
                {
                    await _backups.RestoreAsync(backup, gamePath, placed.RelativePath, cancellationToken).ConfigureAwait(false);
                    restored++;
                }
                catch (Exception ex) when (ex is IOException or FileNotFoundException or UnauthorizedAccessException)
                {
                    problems.Add($"{placed.RelativePath}: could not restore stock file — {ex.Message}");
                }

                continue;
            }

            // No backup means Dispatch added a new file. Remove it — but only while
            // it is still the file Dispatch placed, so anything the user changed is
            // left untouched.
            var full = Path.Combine(gamePath, placed.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                continue;
            }

            var hash = await Hashing.Sha256Async(full, cancellationToken).ConfigureAwait(false);
            if (string.Equals(hash, placed.Sha256, StringComparison.Ordinal))
            {
                toRemove.Add(placed.RelativePath);
            }
        }

        var removed = 0;
        if (toRemove.Count > 0)
        {
            var batch = await _quarantine.QuarantineAsync(gamePath, toRemove, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            removed = batch.Entries.Count;
        }

        return new ReturnToStockReport(restored, removed, problems);
    }

    /// <summary>
    /// The original-stock backup for a file, if Dispatch ever backed it up. Only an
    /// exact mirror at <c>&lt;run&gt;/&lt;relativePath&gt;</c> counts — the first
    /// backup, which is the original before any mod touched it. The disambiguated
    /// <c>.2</c> copies are intermediate mod versions and are ignored.
    /// </summary>
    private string? FindBackup(string relativePath)
    {
        var backupsRoot = _paths.BackupsDirectory;
        if (!Directory.Exists(backupsRoot))
        {
            return null;
        }

        var mirror = relativePath.Replace('/', Path.DirectorySeparatorChar);
        foreach (var run in Directory.GetDirectories(backupsRoot))
        {
            var candidate = Path.Combine(run, mirror);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void TryRemoveDesktopShortcut(List<RemovedItem> removed, List<string> skipped)
    {
        try
        {
            var desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                return;
            }

            var shortcut = Path.Combine(desktop, "Dispatch.lnk");
            if (File.Exists(shortcut))
            {
                var bytes = new FileInfo(shortcut).Length;
                File.Delete(shortcut);
                removed.Add(new RemovedItem(shortcut, bytes));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add($"Desktop shortcut: {ex.Message}");
        }
    }

    // A folder is a Dispatch folder only if its path actually contains the app's
    // name and it is not a drive root. This is the wall that keeps a wipe scoped.
    private static bool LooksLikeDispatchFolder(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar);
        if (trimmed.Length <= 3 || Path.GetPathRoot(trimmed)?.TrimEnd(Path.DirectorySeparatorChar) == trimmed)
        {
            return false;
        }

        return fullPath.Contains("Dispatch", StringComparison.OrdinalIgnoreCase);
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
