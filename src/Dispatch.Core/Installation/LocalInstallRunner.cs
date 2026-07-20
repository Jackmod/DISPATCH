using Dispatch.Core.Catalogue;
using Dispatch.Core.Configuration;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Installation;

/// <summary>
/// Places mods that are already staged into the game folder, journalled and
/// reversible, and writes the install record.
/// </summary>
/// <remarks>
/// This is the real engine, minus acquisition — it takes a set of already-fetched,
/// already-extracted mod folders and does the part that touches the game
/// directory: back up, place, verify, journal, record. Acquisition (the GitHub,
/// HTTP and browser sources) is a separate concern that feeds staging; keeping
/// the placement engine independent of it means the dangerous half is fully
/// testable against a fixture with no network.
///
/// <para>
/// Every file goes through <see cref="FilePlacer"/>, so every write is backed up
/// and journalled. If anything throws, the journal has the record and
/// <see cref="Rollback"/> can undo the run. On success the install record is
/// written with a hash of everything placed, which is what the auditor later
/// checks against.
/// </para>
/// </remarks>
public sealed class LocalInstallRunner
{
    private readonly FilePlacer _placer;
    private readonly IAppPaths _paths;
    private readonly ILogger<LocalInstallRunner> _logger;

    /// <summary>Constructs the runner.</summary>
    public LocalInstallRunner(FilePlacer placer, IAppPaths paths, ILogger<LocalInstallRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(placer);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _placer = placer;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>A mod ready to place: its files already extracted to a folder.</summary>
    /// <param name="Mod">The catalogue definition.</param>
    /// <param name="StagedFolder">Where its extracted files live.</param>
    public sealed record StagedMod(ModDefinition Mod, string StagedFolder);

    /// <summary>
    /// Places every staged mod, in catalogue order, journalled and recorded.
    /// </summary>
    /// <returns>The install record describing everything placed.</returns>
    public async Task<InstallRecord> RunAsync(
        string runId,
        string gamePath,
        string presetId,
        string? gameBuild,
        IReadOnlyList<StagedMod> mods,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(mods);

        var journalPath = Path.Combine(_paths.RunsDirectory, $"{runId}.jsonl");
        await using var journal = RunJournal.Create(journalPath, runId);

        var placed = new List<PlacedFile>();
        var installed = new List<string>();
        var total = CountFiles(mods);
        var done = 0;

        // Catalogue order matters: core first, Callout Interface before its
        // copiers, Search Items Reborn and Ultimate Backup last.
        foreach (var staged in mods.OrderBy(m => m.Mod.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            StripBundledFiles(staged);

            foreach (var file in EnumerateFiles(staged.StagedFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destination = MapDestination(staged, file);
                if (destination is null)
                {
                    continue;
                }

                progress?.Report(new InstallProgress(
                    InstallPhase.PlacingFiles,
                    $"{staged.Mod.Name} — {Path.GetFileName(file)}",
                    ++done, total,
                    total == 0 ? 1 : (double)done / total));

                var outcome = await _placer.PlaceAsync(
                    runId, gamePath, file, destination, staged.Mod.Id, journal, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome != PlacementOutcome.SkippedProtected)
                {
                    var placedPath = Path.Combine(gamePath, destination.Replace('/', Path.DirectorySeparatorChar));
                    var hash = await Hashing.Sha256Async(placedPath, cancellationToken).ConfigureAwait(false);
                    placed.Add(new PlacedFile(destination, hash, staged.Mod.Id));
                }
            }

            installed.Add(staged.Mod.Id);
        }

        var record = new InstallRecord
        {
            GameBuild = gameBuild,
            PresetId = presetId,
            ModIds = installed,
            Files = placed,
        };

        await WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Install run {Run} placed {Files} file(s) from {Mods} mod(s)",
            runId, placed.Count, installed.Count);

        return record;
    }

    /// <summary>Rolls a run back from its journal, restoring the folder to before it started.</summary>
    public async Task<IReadOnlyList<string>> RollbackAsync(
        string runId, string gamePath, CancellationToken cancellationToken = default)
    {
        var journalPath = Path.Combine(_paths.RunsDirectory, $"{runId}.jsonl");
        var rollback = new Rollback(
            new BackupStore(_paths.BackupsDirectory, LoggerFor<BackupStore>()),
            LoggerFor<Rollback>());

        return await rollback.RunAsync(journalPath, gamePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes bundled files a rule marks for stripping, so a stale copy in the
    /// archive never wins over the root copy — the second of the spec's two hard
    /// rules.
    /// </summary>
    private void StripBundledFiles(StagedMod staged)
    {
        var strip = staged.Mod.Placement.StripBeforeExtract;
        if (strip is null || strip.Count == 0)
        {
            return;
        }

        foreach (var file in EnumerateFiles(staged.StagedFolder))
        {
            if (strip.Any(s => string.Equals(Path.GetFileName(file), s, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(file);
                _logger.LogInformation("Stripped bundled {File} from {Mod}", Path.GetFileName(file), staged.Mod.Id);
            }
        }
    }

    /// <summary>Maps a staged file to its destination relative to the game folder, or null to skip it.</summary>
    private static string? MapDestination(StagedMod staged, string stagedFile)
    {
        var rule = staged.Mod.Placement;
        var relativeInStaging = Path.GetRelativePath(staged.StagedFolder, stagedFile).Replace('\\', '/');
        var fileName = Path.GetFileName(stagedFile);

        return rule.Kind switch
        {
            PlacementKind.RootAll when IsExcluded(rule, fileName) => null,
            PlacementKind.RootAll => relativeInStaging,

            PlacementKind.SingleFile or PlacementKind.NamedFiles =>
                rule.Files is not null && rule.Files.Any(f =>
                    string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase))
                    ? Combine(rule.Destination, fileName)
                    : null,

            PlacementKind.FolderContents => Combine(rule.Destination, relativeInStaging),

            _ => null,
        };
    }

    private static bool IsExcluded(PlacementRule rule, string fileName) =>
        rule.Exclude is not null &&
        rule.Exclude.Any(e => string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase));

    private static string Combine(string destination, string relative) =>
        string.IsNullOrEmpty(destination) ? relative : $"{destination.TrimEnd('/')}/{relative}";

    private static IEnumerable<string> EnumerateFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            : [];

    private static int CountFiles(IReadOnlyList<StagedMod> mods) =>
        mods.Sum(m => EnumerateFiles(m.StagedFolder).Count());

    private async Task WriteRecordAsync(InstallRecord record, CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        var temp = _paths.InstallRecordFile + ".tmp";

        await using (var stream = File.Create(temp))
        {
            await System.Text.Json.JsonSerializer.SerializeAsync(
                stream, record,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                },
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _paths.InstallRecordFile, overwrite: true);
    }

    // A minimal logger for the rollback helpers, which take their own typed
    // loggers; in the container these are injected, but the runner constructs
    // them on demand for a rollback so it does not carry three loggers it rarely
    // uses.
    private static ILogger<T> LoggerFor<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}
