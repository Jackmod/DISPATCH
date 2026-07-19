using System.Text.Json;
using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Maintenance;

/// <summary>One file moved to quarantine, and where it came from.</summary>
/// <param name="OriginalRelativePath">Path relative to the game folder it was taken from.</param>
/// <param name="QuarantinedName">Its name inside the quarantine batch folder.</param>
/// <param name="SizeBytes">Size at the time it was moved.</param>
/// <param name="Sha256">Hash at the time it was moved, so a restore can be verified byte-identical.</param>
public sealed record QuarantineEntry(
    string OriginalRelativePath,
    string QuarantinedName,
    long SizeBytes,
    string Sha256);

/// <summary>A single quarantine operation: everything moved in one run.</summary>
/// <param name="Id">Batch identifier, also the folder name under quarantine.</param>
/// <param name="GamePath">The game folder these files were taken from.</param>
/// <param name="CreatedAt">When the batch was made.</param>
/// <param name="Entries">Every file moved.</param>
public sealed record QuarantineBatch(
    string Id,
    string GamePath,
    DateTimeOffset CreatedAt,
    IReadOnlyList<QuarantineEntry> Entries)
{
    /// <summary>Total size of the batch.</summary>
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

/// <summary>Moves files out of the game folder without deleting them, reversibly.</summary>
public interface IQuarantine
{
    /// <summary>
    /// Moves the given files from the game folder into a new quarantine batch.
    /// </summary>
    /// <param name="gamePath">The game folder.</param>
    /// <param name="relativePaths">Files to move, relative to the game folder.</param>
    /// <param name="progress">Reports each file as it moves.</param>
    /// <param name="cancellationToken">Stops after the current file, never mid-move.</param>
    Task<QuarantineBatch> QuarantineAsync(
        string gamePath,
        IReadOnlyList<string> relativePaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every batch on disk, newest first.</summary>
    Task<IReadOnlyList<QuarantineBatch>> ListBatchesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a batch to the game folder it came from, verifying each file by
    /// hash. Returns the files that could not be restored, each with a reason.
    /// </summary>
    Task<IReadOnlyList<string>> RestoreAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a batch. The only place anything is actually removed.</summary>
    Task PurgeAsync(string batchId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The reversible half of the cleaner.
/// </summary>
/// <remarks>
/// Nothing here deletes anything except <see cref="PurgeAsync"/>, and that runs
/// only on an explicit command. Everything else is a move into a batch folder
/// under <c>%LOCALAPPDATA%/Dispatch/quarantine</c>, recorded in a manifest that
/// carries the original path and a hash of every file. A restore uses that
/// manifest to put each file back exactly where it was and verifies it landed
/// byte-identical, so "clean" is always undoable.
///
/// <para>
/// The original relative path is flattened into the quarantined name rather
/// than recreated as a tree, so two files with the same leaf name from
/// different folders cannot collide and overwrite each other inside the batch —
/// which would make one of them unrecoverable.
/// </para>
/// </remarks>
public sealed class Quarantine : IQuarantine
{
    private const string ManifestName = "manifest.json";

    private readonly string _root;
    private readonly ILogger<Quarantine> _logger;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Constructs the quarantine over its storage root.</summary>
    /// <param name="quarantineRoot">Where batches live. Usually <c>%LOCALAPPDATA%/Dispatch/quarantine</c>.</param>
    public Quarantine(string quarantineRoot, ILogger<Quarantine> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quarantineRoot);
        ArgumentNullException.ThrowIfNull(logger);

        _root = quarantineRoot;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<QuarantineBatch> QuarantineAsync(
        string gamePath,
        IReadOnlyList<string> relativePaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var fullGamePath = Path.GetFullPath(gamePath);
        var batchId = NewBatchId();
        var batchDir = Path.Combine(_root, batchId);
        Directory.CreateDirectory(batchDir);

        var entries = new List<QuarantineEntry>();
        var index = 0;

        foreach (var relative in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalised = StockManifest.Normalise(relative);

            // Nothing protected is ever moved, whatever a caller passes. This is
            // a second wall behind the scanner: even a bug that put a save in
            // the removal list cannot act on it here.
            if (StockManifest.IsProtected(normalised))
            {
                _logger.LogWarning("Refused to quarantine protected path {Path}", normalised);
                continue;
            }

            var source = Path.GetFullPath(Path.Combine(fullGamePath, normalised));

            // Path-traversal defence: a crafted relative path must not reach
            // outside the game folder and move something arbitrary off the disk.
            if (!source.StartsWith(fullGamePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Refused to quarantine out-of-tree path {Path}", source);
                continue;
            }

            if (!File.Exists(source))
            {
                continue;
            }

            progress?.Report(normalised);

            // Flatten the path so leaf-name collisions across folders cannot
            // overwrite each other inside the batch.
            var quarantinedName = $"{index:D4}__{Flatten(normalised)}";
            var destination = Path.Combine(batchDir, quarantinedName);

            var hash = await Hashing.Sha256Async(source, cancellationToken).ConfigureAwait(false);
            var size = new FileInfo(source).Length;

            File.Move(source, destination, overwrite: false);

            entries.Add(new QuarantineEntry(normalised, quarantinedName, size, hash));
            index++;
        }

        var batch = new QuarantineBatch(batchId, fullGamePath, DateTimeOffset.UtcNow, entries);
        await WriteManifestAsync(batchDir, batch, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Quarantined {Count} file(s) into batch {Batch}", entries.Count, batchId);
        return batch;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuarantineBatch>> ListBatchesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var batches = new List<QuarantineBatch>();

        foreach (var dir in Directory.GetDirectories(_root))
        {
            var manifest = Path.Combine(dir, ManifestName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(manifest);
                var batch = await JsonSerializer
                    .DeserializeAsync<QuarantineBatch>(stream, Json, cancellationToken)
                    .ConfigureAwait(false);

                if (batch is not null)
                {
                    batches.Add(batch);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Quarantine batch {Dir} has an unreadable manifest", dir);
            }
        }

        return batches.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RestoreAsync(string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var batchDir = Path.Combine(_root, batchId);
        var manifestPath = Path.Combine(batchDir, ManifestName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"No such quarantine batch: {batchId}");
        }

        QuarantineBatch batch;
        await using (var stream = File.OpenRead(manifestPath))
        {
            batch = (await JsonSerializer.DeserializeAsync<QuarantineBatch>(stream, Json, cancellationToken)
                .ConfigureAwait(false))!;
        }

        var failures = new List<string>();

        foreach (var entry in batch.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var quarantined = Path.Combine(batchDir, entry.QuarantinedName);
            var destination = Path.Combine(batch.GamePath, entry.OriginalRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(quarantined))
            {
                failures.Add($"{entry.OriginalRelativePath}: no longer in quarantine");
                continue;
            }

            // Never clobber a file that has reappeared at the destination since
            // the clean — the user may have reinstalled it, and their version
            // wins over the quarantined copy.
            if (File.Exists(destination))
            {
                failures.Add($"{entry.OriginalRelativePath}: a file is already there, left untouched");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(quarantined, destination, overwrite: false);

            // Verify the restore landed byte-identical to what was taken. A bad
            // copy is worse than a loud failure.
            var restoredHash = await Hashing.Sha256Async(destination, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(restoredHash, entry.Sha256, StringComparison.Ordinal))
            {
                File.Delete(destination);
                failures.Add($"{entry.OriginalRelativePath}: restored copy did not match, reverted");
                continue;
            }

            File.Delete(quarantined);
        }

        // The batch is emptied only if everything came back.
        if (failures.Count == 0)
        {
            await PurgeAsync(batchId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Restored batch {Batch}: {Restored} restored, {Failed} failed",
            batchId, batch.Entries.Count - failures.Count, failures.Count);

        return failures;
    }

    /// <inheritdoc />
    public Task PurgeAsync(string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var batchDir = Path.Combine(_root, batchId);
        if (Directory.Exists(batchDir))
        {
            Directory.Delete(batchDir, recursive: true);
            _logger.LogInformation("Purged quarantine batch {Batch}", batchId);
        }

        return Task.CompletedTask;
    }

    private async Task WriteManifestAsync(string batchDir, QuarantineBatch batch, CancellationToken cancellationToken)
    {
        var path = Path.Combine(batchDir, ManifestName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, batch, Json, cancellationToken).ConfigureAwait(false);
    }

    // A sortable, unique batch id. No Date.Now in Core is fine here — this is a
    // real runtime path, not a workflow script.
    private static string NewBatchId() =>
        $"clean-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    private static string Flatten(string relativePath) =>
        relativePath.Replace('/', '_').Replace('\\', '_');
}
