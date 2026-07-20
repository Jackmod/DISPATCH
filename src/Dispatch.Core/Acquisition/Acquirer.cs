using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>The outcome of acquiring one mod.</summary>
/// <param name="Mod">The mod acquired.</param>
/// <param name="StagedFolder">Where its extracted files landed, ready to place.</param>
/// <param name="ResolvedVersion">The version fetched, when the source could name it.</param>
/// <param name="FromCache">True when a previously downloaded archive was reused.</param>
public sealed record AcquiredMod(ModDefinition Mod, string StagedFolder, string? ResolvedVersion, bool FromCache);

/// <summary>
/// Fetches a mod and unpacks it into staging, ready for the placement engine.
/// </summary>
/// <remarks>
/// This is the bridge between the catalogue and <c>LocalInstallRunner</c>: it
/// picks the right <see cref="IDownloadSource"/>, downloads to the archives cache
/// (so a repair or a re-run needs no network), and extracts into this run's
/// staging folder. The placement engine downstream never knows how a mod arrived
/// — only that its files are sitting in a folder.
///
/// <para>
/// A mod whose source is <see cref="SourceKind.Browser"/> has no automated source
/// and is not acquired here; the caller decides how to surface that. A mod whose
/// archive is already in the cache is extracted straight from it, skipping the
/// download entirely — the archives directory is the resilience story for "the
/// author's host went down after you first installed."
/// </para>
/// </remarks>
public sealed class Acquirer
{
    private readonly IReadOnlyList<IDownloadSource> _sources;
    private readonly IArchiveExtractor _extractor;
    private readonly IAppPaths _paths;
    private readonly AcquisitionOptions _options;
    private readonly ILogger<Acquirer> _logger;

    /// <summary>Constructs the acquirer over the available sources.</summary>
    public Acquirer(
        IEnumerable<IDownloadSource> sources,
        IArchiveExtractor extractor,
        IAppPaths paths,
        ILogger<Acquirer> logger,
        AcquisitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources.ToList();
        _extractor = extractor;
        _paths = paths;
        _options = options ?? new AcquisitionOptions();
        _logger = logger;
    }

    /// <summary>Whether any usable source can fetch this mod without a human.</summary>
    public bool CanAcquire(ModDefinition mod) => UsableSources().Any(s => s.CanHandle(mod));

    /// <summary>
    /// The sources allowed for this run. Offline drops the network sources, so an
    /// install proceeds entirely from the bundled pack without a download.
    /// </summary>
    private IEnumerable<IDownloadSource> UsableSources() =>
        _options.Offline
            ? _sources.Where(s => s.Kind == SourceKind.Browser)
            : _sources;

    /// <summary>
    /// Acquires one mod: download (or reuse the cache) then extract into staging.
    /// </summary>
    /// <param name="mod">What to fetch.</param>
    /// <param name="staging">This run's staging area.</param>
    /// <param name="progress">Receives download progress, when the source reports it.</param>
    /// <param name="cancellationToken">Stops between download and extract.</param>
    /// <returns>The staged mod, ready to place.</returns>
    /// <exception cref="AcquisitionException">No source could fetch it, or the archive was unreadable.</exception>
    public async Task<AcquiredMod> AcquireAsync(
        ModDefinition mod,
        StagingArea staging,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(staging);

        var capable = OrderedSources(mod);
        if (capable.Count == 0)
        {
            throw new AcquisitionException(
                mod.Id, "it must be downloaded by hand — its host has no link Dispatch can follow automatically.");
        }

        _paths.EnsureCreated();

        string archivePath;
        string? version = null;
        var fromCache = false;

        // A version-locked mod — the script hooks — is pulled fresh every time so
        // it always matches the current game build; its local cache is skipped and
        // the pack is only a fallback. Everything else reuses a cached archive
        // first, so a repair or re-run needs no network.
        var preferFresh = PrefersFresh(mod);
        var cached = preferFresh ? null : FindCachedArchive(mod);

        if (cached is not null)
        {
            _logger.LogInformation("Reusing cached archive for {Mod}: {Path}", mod.Id, cached);
            archivePath = cached;
            fromCache = true;
        }
        else
        {
            (archivePath, version, fromCache) = await DownloadFirstWorkingAsync(
                mod, capable, progress, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stagedFolder = staging.PrepareModDirectory(mod.Id);

        try
        {
            var count = await _extractor
                .ExtractAsync(archivePath, stagedFolder, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Staged {Mod}: {Count} file(s) from {Archive}", mod.Id, count, Path.GetFileName(archivePath));
        }
        catch (UnsafeArchivePathException ex)
        {
            // A traversal attempt is the one archive failure worth naming loudly;
            // it means the archive tried to write outside staging.
            throw new AcquisitionException(
                mod.Id, "its archive tried to write files outside the install folder and was rejected.", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            // A cached archive that will not open is more likely corrupt than the
            // format being unsupported — drop it so the next run re-downloads.
            if (fromCache)
            {
                TryDelete(archivePath);
                throw new AcquisitionException(
                    mod.Id, "the saved copy was corrupt and has been discarded — run the install again to re-fetch it.", ex);
            }

            throw new AcquisitionException(mod.Id, "its archive could not be read; the download may be incomplete.", ex);
        }

        return new AcquiredMod(mod, stagedFolder, version, fromCache);
    }

    /// <summary>
    /// The sources that can fetch this mod, in preference order. For a
    /// version-locked mod, GitHub is tried first so the copy is always current;
    /// the bundled pack then serves as an offline fallback.
    /// </summary>
    private IReadOnlyList<IDownloadSource> OrderedSources(ModDefinition mod)
    {
        var capable = UsableSources().Where(s => s.CanHandle(mod)).ToList();

        return PrefersFresh(mod)
            ? capable.OrderByDescending(s => s.Kind == SourceKind.GitHubRelease).ToList()
            : capable;
    }

    /// <summary>
    /// Whether a mod should be pulled fresh rather than served from the pack or
    /// cache. Never offline — offline uses whatever the pack holds.
    /// </summary>
    private bool PrefersFresh(ModDefinition mod) =>
        !_options.Offline && mod.Anchor == CompatibilityAnchor.GameBuild;

    /// <summary>
    /// Downloads from the first source that succeeds, falling through the list on
    /// failure, and finally to a cached copy if every source is unreachable.
    /// </summary>
    private async Task<(string ArchivePath, string? Version, bool FromCache)> DownloadFirstWorkingAsync(
        ModDefinition mod,
        IReadOnlyList<IDownloadSource> sources,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var modCacheDir = Path.Combine(_paths.ArchivesDirectory, mod.Id);
        Directory.CreateDirectory(modCacheDir);

        AcquisitionException? last = null;

        foreach (var source in sources)
        {
            try
            {
                var result = await source
                    .DownloadAsync(mod, modCacheDir, progress, cancellationToken)
                    .ConfigureAwait(false);
                return (result.ArchivePath, result.ResolvedVersion, false);
            }
            catch (AcquisitionException ex)
            {
                _logger.LogWarning("Source {Source} could not fetch {Mod}: {Reason}", source.Kind, mod.Id, ex.Reason);
                last = ex;
            }
        }

        // Every source failed. A previously downloaded copy is better than none —
        // this is what keeps a script-hook update from failing the whole install
        // when GitHub is briefly unreachable.
        var cached = FindCachedArchive(mod);
        if (cached is not null)
        {
            _logger.LogInformation("All sources failed for {Mod}; using cached archive {Path}", mod.Id, cached);
            return (cached, null, true);
        }

        throw last ?? new AcquisitionException(mod.Id, "no source could fetch it.");
    }

    /// <summary>
    /// Returns the newest cached archive for a mod, or null when none is cached.
    /// </summary>
    /// <remarks>
    /// Newest by write time so that if a mod was re-downloaded after an update,
    /// the fresher archive wins. The <c>.partial</c> files a dropped download
    /// leaves behind are never returned.
    /// </remarks>
    private string? FindCachedArchive(ModDefinition mod)
    {
        var dir = Path.Combine(_paths.ArchivesDirectory, mod.Id);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir)
            .Where(f => !f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not delete corrupt cached archive {Path}", path);
        }
    }
}
