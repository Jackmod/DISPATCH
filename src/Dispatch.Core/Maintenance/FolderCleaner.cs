using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Maintenance;

/// <summary>How confident the cleaner is that a file is a mod file.</summary>
public enum CleanTier
{
    /// <summary>Recognised as belonging to a mod Dispatch knows. Safe to preselect.</summary>
    Known,

    /// <summary>Matches a mod-shaped pattern but is not recognised. Preselected with caution.</summary>
    Likely,

    /// <summary>Matches nothing. Never preselected; listed for the user to decide.</summary>
    Unknown,
}

/// <summary>One candidate for removal.</summary>
/// <param name="RelativePath">Path relative to the game folder.</param>
/// <param name="SizeBytes">Size on disk.</param>
/// <param name="Tier">How confident the cleaner is.</param>
/// <param name="Reason">Why it was classified this way, shown in the preview.</param>
public sealed record CleanCandidate(
    string RelativePath,
    long SizeBytes,
    CleanTier Tier,
    string Reason)
{
    /// <summary>Whether this is preselected in the preview.</summary>
    /// <remarks>
    /// Unknown is never preselected. The user has to actively choose anything
    /// the cleaner cannot account for, because that is precisely the set most
    /// likely to contain something they care about.
    /// </remarks>
    public bool IsPreselected => Tier is CleanTier.Known or CleanTier.Likely;
}

/// <summary>The result of a scan. Nothing has been moved at this point.</summary>
/// <param name="Candidates">Everything found, in tier then path order.</param>
/// <param name="FilesScanned">How many files were examined.</param>
/// <param name="Protected">Paths that were found and deliberately left alone.</param>
public sealed record CleanPlan(
    IReadOnlyList<CleanCandidate> Candidates,
    int FilesScanned,
    IReadOnlyList<string> Protected)
{
    /// <summary>Total size of everything preselected.</summary>
    public long PreselectedBytes =>
        Candidates.Where(c => c.IsPreselected).Sum(c => c.SizeBytes);

    /// <summary>Candidates grouped by tier, for the preview tree.</summary>
    public IReadOnlyDictionary<CleanTier, IReadOnlyList<CleanCandidate>> ByTier =>
        Candidates
            .GroupBy(c => c.Tier)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CleanCandidate>)g.ToList());
}

/// <summary>
/// Works out what in a game folder does not belong to a stock install.
/// </summary>
/// <remarks>
/// This is the most dangerous thing in the application, so it is built as two
/// separate halves: this class only ever <em>reads</em> and produces a plan.
/// Nothing here can remove anything. Moving files is a separate, explicit step
/// the user triggers after reading the plan.
///
/// <para>
/// The order of checks matters and is deliberate. Protected wins over
/// everything, then stock, then recognition. Reversing any pair produces a
/// version that will eventually delete somebody's saves.
/// </para>
/// </remarks>
public sealed class FolderCleaner
{
    private readonly ILogger<FolderCleaner> _logger;
    private readonly IReadOnlySet<string> _knownModFiles;

    /// <summary>Extensions that mark a file as mod-shaped when found at the root.</summary>
    private static readonly HashSet<string> ModExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".asi", ".dll", ".ini", ".log", ".xml",
    };

    /// <summary>Folders that only exist because a mod created them.</summary>
    private static readonly HashSet<string> ModFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugins", "scripts", "lspdfr", "mods", "ragepluginhook",
    };

    /// <summary>
    /// Loaders and tools universally known to be mods when found at the game
    /// root, so they can be recognised even when Dispatch did not install them.
    /// </summary>
    /// <remarks>
    /// The game root is the one place stock files and mod files sit side by side —
    /// a loose <c>.dll</c> there is as likely to be a graphics or launcher library
    /// as a mod. So only files on this list are treated as mods at the root; every
    /// other unrecognised root file is left for the user to judge rather than
    /// preselected. This is the difference between offering to remove
    /// <c>ScriptHookV.dll</c> and offering to remove <c>libcurl.dll</c>.
    /// </remarks>
    private static readonly HashSet<string> KnownRootModFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dinput8.dll", "scripthookv.dll", "scripthookv.ini",
        "scripthookvdotnet.asi", "scripthookvdotnet2.dll", "scripthookvdotnet3.dll",
        "scripthookvdotnet.ini", "scripthookvdotnet.json",
        "openiv.asi", "asiloader.dll", "packfilelimitadjuster.asi",
        "trainerv.asi", "trainerv.ini", "nativetrainer.asi", "menyoo.asi",
        "ragenativeui.dll", "ragepluginhook.exe", "rageplugin.hook.exe",
        "iplloader.asi", "gtavlauncher_orig.exe", "community_races.asi",
    };

    /// <summary>Constructs the cleaner.</summary>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="knownModFiles">
    /// Relative paths Dispatch itself installed, or recognises from the
    /// catalogue. These become the Known tier.
    /// </param>
    public FolderCleaner(ILogger<FolderCleaner> logger, IEnumerable<string>? knownModFiles = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _knownModFiles = (knownModFiles ?? [])
            .Select(StockManifest.Normalise)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a game folder and produces a plan. Reads only; removes nothing.
    /// </summary>
    /// <param name="gamePath">Root of the installation.</param>
    /// <param name="progress">Receives the running file count.</param>
    /// <param name="cancellationToken">Stops the scan.</param>
    public CleanPlan Scan(
        string gamePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        if (!Directory.Exists(gamePath))
        {
            throw new DirectoryNotFoundException($"No such folder: {gamePath}");
        }

        var candidates = new List<CleanCandidate>();
        var protectedPaths = new List<string>();
        var scanned = 0;

        foreach (var file in EnumerateFiles(gamePath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            scanned++;
            if (scanned % 250 == 0)
            {
                progress?.Report(scanned);
            }

            var relative = StockManifest.Normalise(Path.GetRelativePath(gamePath, file));

            // Protected first, and unconditionally. Nothing later can override
            // this, and there is no UI path that can either.
            if (StockManifest.IsProtected(relative))
            {
                protectedPaths.Add(relative);
                continue;
            }

            if (StockManifest.IsStock(relative))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable means unknown, which means the user decides.
                _logger.LogDebug(ex, "Could not size {Path}", relative);
                size = 0;
            }

            candidates.Add(Classify(relative, size));
        }

        progress?.Report(scanned);

        var ordered = candidates
            .OrderBy(c => c.Tier)
            .ThenBy(c => c.RelativePath, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "Scanned {Scanned} files: {Known} known, {Likely} likely, {Unknown} unknown, {Protected} protected",
            scanned,
            ordered.Count(c => c.Tier == CleanTier.Known),
            ordered.Count(c => c.Tier == CleanTier.Likely),
            ordered.Count(c => c.Tier == CleanTier.Unknown),
            protectedPaths.Count);

        return new CleanPlan(ordered, scanned, protectedPaths);
    }

    /// <summary>
    /// Removes directories left empty under the game folder once mod files have been
    /// moved out — the <c>plugins</c>/<c>scripts</c>/<c>lspdfr</c> trees a mod
    /// created, and any empty subfolder within them. Returns how many were removed.
    /// </summary>
    /// <remarks>
    /// Safe by construction: only a directory that holds nothing is removed, so a
    /// stock folder with real content is always kept, and a folder holding a file
    /// the user chose not to clean is left in place. The root is never touched, and
    /// reparse points (a junctioned library) are skipped so the walk cannot wander
    /// off onto another disk.
    /// </remarks>
    public int PruneEmptyDirectories(string gamePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        if (!Directory.Exists(gamePath))
        {
            return 0;
        }

        var removed = 0;

        // Deepest first (longest path first), so a parent that becomes empty once
        // its children are gone is caught in the same pass.
        var directories = SafeAllDirectories(gamePath)
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dir in directories)
        {
            try
            {
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    continue;
                }

                Directory.Delete(dir, recursive: false);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not remove empty folder {Dir}", dir);
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Removed} empty folder(s) left behind by mods", removed);
        }

        return removed;
    }

    private static IEnumerable<string> SafeAllDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Decides which tier a non-stock, non-protected file belongs to.</summary>
    private CleanTier ClassifyTier(string relative, out string reason)
    {
        if (_knownModFiles.Contains(relative))
        {
            reason = "Installed by Dispatch, or recognised from the mod catalogue.";
            return CleanTier.Known;
        }

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var extension = Path.GetExtension(relative);

        if (segments.Length > 1 && ModFolders.Contains(segments[0]))
        {
            reason = $"Inside '{segments[0]}', a folder only a mod creates.";
            return CleanTier.Likely;
        }

        // A loose file at the root is only treated as a mod when it is one of the
        // loaders everyone recognises. Every other unrecognised root file is left
        // Unknown, because the root also holds stock game and launcher libraries
        // that must never be preselected for removal.
        if (segments.Length == 1 && ModExtensions.Contains(extension))
        {
            if (KnownRootModFiles.Contains(segments[0]))
            {
                reason = "A known mod loader or tool at the game root.";
                return CleanTier.Likely;
            }

            reason = "A loose file at the game root, where stock game files also live. "
                     + "Dispatch will not guess — decide for yourself.";
            return CleanTier.Unknown;
        }

        reason = "Not part of a stock install, and not recognised. Dispatch will not guess.";
        return CleanTier.Unknown;
    }

    private CleanCandidate Classify(string relative, long size)
    {
        var tier = ClassifyTier(relative, out var reason);
        return new CleanCandidate(relative, size, tier, reason);
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        // ReparsePoint is skipped so a junctioned library does not send the
        // scan somewhere else on the disk entirely.
        return Directory.EnumerateFiles(root, "*", options);
    }
}
