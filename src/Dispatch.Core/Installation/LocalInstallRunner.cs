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

            // Mods we cannot auto-install — OpenIV packages, add-on DLCs, loose
            // ymaps — go wholesale to the import folder for the user to apply in
            // OpenIV. Nothing of theirs touches the game folder.
            if (staged.Mod.Placement.Kind == PlacementKind.ManualImport)
            {
                SetAsideForManualImport(staged);
                installed.Add(staged.Mod.Id);
                continue;
            }

            StripBundledFiles(staged);

            // Set aside anything bound for OpenIV before placing — those never go
            // into the game folder, they go to the import folder for the user.
            SetAsideOpenIvFiles(staged);

            // Resolve where this mod's files actually start inside the archive. A
            // rule with a SourceFolder ("Installation Files/Grand Theft Auto 5")
            // means only that subtree is placed; AutoDetect strips wrapper folders
            // down to the recognised game content.
            var contentRoot = staged.Mod.Placement.Kind == PlacementKind.AutoDetect
                ? ResolveAutoRoot(staged.StagedFolder)
                : ResolveContentRoot(staged.StagedFolder, staged.Mod.Placement.SourceFolder);

            foreach (var file in EnumerateFiles(staged.StagedFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsOpenIvFile(file) || IsJunk(file))
                {
                    continue;
                }

                var destination = MapDestination(staged, contentRoot, file);
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
    /// Shared libraries that are installed once, from their own mod, and must
    /// never be carried in by another mod that happens to bundle a stale copy.
    /// The guide's repeated "deselect RativeUI.dll" is exactly this — every
    /// plugin that ships RAGENativeUI must drop its copy so the managed one wins.
    /// </summary>
    private static readonly HashSet<string> GloballyManagedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "RAGENativeUI.dll",
        "RativeUI.dll",
        "RageNativeUI.dll",
    };

    /// <summary>
    /// Removes files that must not travel with a mod: the ones its rule marks for
    /// stripping, and any globally-managed shared library it does not itself own.
    /// A stale bundled copy overwriting the canonical one is a silent failure the
    /// guide spends real effort avoiding by hand.
    /// </summary>
    private void StripBundledFiles(StagedMod staged)
    {
        var explicitStrip = staged.Mod.Placement.StripBeforeExtract ?? [];
        var owned = staged.Mod.Placement.Files ?? [];

        foreach (var file in EnumerateFiles(staged.StagedFolder))
        {
            var name = Path.GetFileName(file);

            var markedForStrip = explicitStrip.Any(s => string.Equals(name, s, StringComparison.OrdinalIgnoreCase));

            // A managed shared library is stripped from every mod except the one
            // whose rule actually names it as a file to place — its true owner.
            var redundantSharedLib =
                GloballyManagedAssemblies.Contains(name)
                && !owned.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

            if (markedForStrip || redundantSharedLib)
            {
                File.Delete(file);
                _logger.LogInformation(
                    "Stripped bundled {File} from {Mod} ({Reason})",
                    name, staged.Mod.Id, redundantSharedLib ? "managed shared library" : "rule");
            }
        }
    }

    /// <summary>
    /// Maps a staged file to its destination relative to the game folder, or null
    /// to skip it. <paramref name="contentRoot"/> is where placement is measured
    /// from — the SourceFolder subtree when the rule names one, else the archive
    /// root.
    /// </summary>
    private static string? MapDestination(StagedMod staged, string? contentRoot, string stagedFile)
    {
        var rule = staged.Mod.Placement;
        var fileName = Path.GetFileName(stagedFile);

        switch (rule.Kind)
        {
            // Named files are found by name anywhere in the archive, so a
            // SourceFolder is only a hint for them and never needs resolving.
            case PlacementKind.SingleFile or PlacementKind.NamedFiles:
                return rule.Files is not null && rule.Files.Any(f =>
                    string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase))
                    ? Combine(rule.Destination, fileName)
                    : null;

            case PlacementKind.RootAll or PlacementKind.FolderContents or PlacementKind.AutoDetect:
                // A named SourceFolder that was not found means nothing to place.
                if (contentRoot is null || !IsUnder(contentRoot, stagedFile))
                {
                    return null;
                }

                var relative = Path.GetRelativePath(contentRoot, stagedFile).Replace('\\', '/');
                if ((rule.Kind == PlacementKind.RootAll || rule.Kind == PlacementKind.AutoDetect)
                    && IsExcluded(rule, fileName))
                {
                    return null;
                }

                return Combine(rule.Destination, relative);

            default:
                return null;
        }
    }

    /// <summary>
    /// Finds the directory a mod's files should be measured from. With no
    /// SourceFolder that is the archive root; with one it is that folder wherever
    /// it sits in the tree, so a wrapper folder around the archive is transparent.
    /// Returns null when a named SourceFolder is not present at all.
    /// </summary>
    private static string? ResolveContentRoot(string stagedFolder, string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return stagedFolder;
        }

        var target = sourceFolder.Replace('\\', '/').Trim('/');

        var direct = Path.Combine(stagedFolder, target.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(direct))
        {
            return direct;
        }

        // Not at the root — search for it, tolerating a wrapper folder, and take
        // the shallowest match so a coincidental deeper folder of the same name
        // cannot win.
        return Directory
            .EnumerateDirectories(stagedFolder, "*", SearchOption.AllDirectories)
            .Where(dir =>
            {
                var rel = Path.GetRelativePath(stagedFolder, dir).Replace('\\', '/');
                return rel.Equals(target, StringComparison.OrdinalIgnoreCase)
                       || rel.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(dir => dir.Length)
            .FirstOrDefault();
    }

    private static bool IsUnder(string root, string file)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    // The folders that are game content and must be merged as-is, never descended
    // into. Reaching one of these means the wrapper stripping has gone far enough.
    private static readonly HashSet<string> GameContentFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugins", "scripts", "lspdfr", "mods", "ragepluginhook",
        "x64", "x64a", "x64b", "x64c", "x64d", "x64e", "x64f", "x64g", "update", "common",
    };

    /// <summary>
    /// For AutoDetect: descends through single wrapper folders (a "Stop The Ped"
    /// or "Installation Files/Grand Theft Auto 5" that wraps the real content)
    /// until it reaches the content itself — a folder holding several entries, any
    /// loose file, or a recognised game folder. That level is what merges into the
    /// game root.
    /// </summary>
    private static string ResolveAutoRoot(string stagedFolder)
    {
        var current = stagedFolder;

        for (var depth = 0; depth < 8; depth++)
        {
            var files = Directory.EnumerateFiles(current).Where(f => !IsJunk(f)).ToList();
            var dirs = Directory.EnumerateDirectories(current).ToList();

            // Content reached: any loose file here, or anything other than exactly
            // one subfolder, or that one subfolder is game content to keep intact.
            if (files.Count > 0 || dirs.Count != 1 || GameContentFolders.Contains(Path.GetFileName(dirs[0])))
            {
                return current;
            }

            current = dirs[0];
        }

        return current;
    }

    private static bool IsExcluded(PlacementRule rule, string fileName) =>
        rule.Exclude is not null &&
        rule.Exclude.Any(e => string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase));

    // ===== Junk and OpenIV routing =======================================

    private static readonly string[] JunkExtensions = [".url", ".nfo", ".lnk"];
    private static readonly string[] DocExtensions = [".txt", ".md", ".rtf", ".pdf", ".doc", ".docx", ".htm", ".html"];
    private static readonly string[] DocNameHints =
        ["readme", "read me", "read_me", "license", "licence", "changelog", "change log",
         "credits", "instructions", "how to install", "howtoinstall", "installation"];

    /// <summary>
    /// Documentation and shortcut cruft that should never reach a game folder.
    /// Conservative on purpose: a plain <c>.txt</c> or <c>.ini</c> a mod actually
    /// reads is kept — only files named like documentation are dropped.
    /// </summary>
    private static bool IsJunk(string file)
    {
        // A version-control folder carried inside an archive (SPGR ships a whole
        // .git tree) is never game content and must never be copied anywhere.
        if (file.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (JunkExtensions.Contains(extension))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return DocExtensions.Contains(extension)
               && DocNameHints.Any(hint => name.Contains(hint, StringComparison.Ordinal));
    }

    // .oiv is OpenIV's own package format — a whole mod meant to be run through
    // OpenIV's Package Installer, never unzipped into the game folder.
    private static readonly string[] OpenIvExtensions =
        [".ytd", ".ydr", ".ydd", ".yft", ".ybn", ".ycd", ".ypt", ".ymap", ".ytyp", ".rpf", ".oiv"];

    // OpenIV's own ASI loaders. Prefix-matched so versioned names are caught too.
    private static readonly string[] OpenIvAsiPrefixes = ["openiv", "opencamera"];

    /// <summary>
    /// Whether a file has to go through OpenIV rather than into the game folder —
    /// RPF/resource content, <c>.oiv</c> packages, and OpenIV's own ASI loaders.
    /// </summary>
    /// <remarks>
    /// The <c>.asi</c> test is deliberately narrow: only OpenIV's own loaders
    /// (OpenIV.asi, OpenCamera.asi and their variants) are set aside. Every other
    /// <c>.asi</c> — ScriptHookVDotNet.asi, TrainerV.asi, and the rest — is a
    /// normal game-root mod and must still be placed.
    /// </remarks>
    private static bool IsOpenIvFile(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (OpenIvExtensions.Contains(extension))
        {
            return true;
        }

        if (extension == ".asi")
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return OpenIvAsiPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
        }

        return false;
    }

    /// <summary>
    /// Copies a mod's OpenIV-bound files into the import folder, under the mod's
    /// name and keeping their layout, so the user can see what belongs where.
    /// Never deletes from staging — placement simply skips them afterwards.
    /// </summary>
    private void SetAsideOpenIvFiles(StagedMod staged)
    {
        var openIvFiles = EnumerateFiles(staged.StagedFolder).Where(IsOpenIvFile).ToList();
        if (openIvFiles.Count == 0)
        {
            return;
        }

        var modFolder = Path.Combine(_paths.OpenIvImportDirectory, Sanitize(staged.Mod.Id));
        Directory.CreateDirectory(modFolder);

        foreach (var file in openIvFiles)
        {
            var relative = Path.GetRelativePath(staged.StagedFolder, file);
            var destination = Path.Combine(modFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        _logger.LogInformation(
            "Set aside {Count} OpenIV file(s) for {Mod} in the import folder", openIvFiles.Count, staged.Mod.Id);
    }

    /// <summary>
    /// Copies an entire manual-import mod into the import folder, laid out as
    /// shipped, so the user can apply it through OpenIV. Junk (readmes, version
    /// control) is left behind; nothing goes to the game folder.
    /// </summary>
    private void SetAsideForManualImport(StagedMod staged)
    {
        var files = EnumerateFiles(staged.StagedFolder).Where(f => !IsJunk(f)).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var modFolder = Path.Combine(_paths.OpenIvImportDirectory, Sanitize(staged.Mod.Id));
        Directory.CreateDirectory(modFolder);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(staged.StagedFolder, file);
            var destination = Path.Combine(modFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        _logger.LogInformation(
            "Set aside {Count} file(s) for manual OpenIV import: {Mod}", files.Count, staged.Mod.Id);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

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
