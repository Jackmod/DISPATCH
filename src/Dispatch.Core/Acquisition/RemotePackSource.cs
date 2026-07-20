using Dispatch.Core.Catalogue;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>One hosted archive: the file name it is served under and the direct URL to it.</summary>
/// <param name="File">The archive's file name, as hosted (e.g. "ELS V1.05.rar").</param>
/// <param name="Url">A direct download URL the app can fetch with no interaction.</param>
public sealed record RemotePackEntry(string File, string Url);

/// <summary>
/// The remote mod pack index: every archive hosted for on-demand download, as a
/// list of (file name, direct URL) pairs.
/// </summary>
/// <remarks>
/// A distinct type so the container injects it without an
/// <c>IEnumerable&lt;string&gt;</c> ambiguity, mirroring <see cref="ModPackRoots"/>.
/// It is loaded once from the small <c>remote-pack.json</c> shipped beside the
/// executable — the only pack data a thin installer carries.
/// </remarks>
/// <param name="Entries">Every hosted archive.</param>
public sealed record RemotePackIndex(IReadOnlyList<RemotePackEntry> Entries);

/// <summary>
/// Resolves a mod's archive from a hosted pack over HTTP, downloading only the
/// mods the user actually selected.
/// </summary>
/// <remarks>
/// This is what makes a <em>thin</em> installer possible. Instead of shipping
/// every archive beside the executable, the app carries only a small index of
/// (file name → direct URL) pairs and fetches each selected mod on demand. The
/// engine already acquires only the mods in the request, so nothing the user did
/// not tick is ever downloaded.
///
/// <para>
/// The same <see cref="ModArchiveMatcher"/> the bundled pack uses maps hosted file
/// names to mods, so hosting needs no per-mod bookkeeping: upload the archives
/// under their original (or provider-sanitised) names and the matcher works out
/// which is which. Because the matcher reduces both sides to a separator- and
/// version-free form, a host that rewrites "world of variety 10.1.zip" to
/// "world.of.variety.10.1.zip" still matches.
/// </para>
///
/// <para>
/// Registered after <see cref="BundledModSource"/>, so a locally dropped archive
/// still overrides the hosted copy, and treated as a normal network source, so it
/// is skipped in offline mode, which is pack-only by definition.
/// </para>
/// </remarks>
public sealed class RemotePackSource : IDownloadSource
{
    private readonly IReadOnlyDictionary<string, string> _urlByFile;
    private readonly IReadOnlyDictionary<string, ModArchiveMatcher.Assignment> _matches;
    private readonly HttpFileDownloader _downloader;
    private readonly ILogger<RemotePackSource> _logger;

    /// <summary>Constructs the source over the hosted index.</summary>
    public RemotePackSource(RemotePackIndex index, HttpFileDownloader downloader, ILogger<RemotePackSource> logger)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(logger);

        _downloader = downloader;
        _logger = logger;

        // Last entry wins on a duplicate file name, so a re-upload that appends a
        // fresh URL cannot leave two live copies fighting over the same name.
        _urlByFile = index.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.File) && !string.IsNullOrWhiteSpace(e.Url))
            .GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Url, StringComparer.OrdinalIgnoreCase);

        // The index is fixed for the app's lifetime, so the archive→mod match is
        // computed once here rather than on every CanHandle.
        _matches = ModArchiveMatcher.Match(_urlByFile.Keys, ModCatalogue.Mods.Values).Matches;

        if (_urlByFile.Count > 0)
        {
            _logger.LogInformation(
                "Remote pack: {Files} hosted archive(s), {Matched} matched to mods",
                _urlByFile.Count, _matches.Count);
        }
    }

    /// <inheritdoc />
    public SourceKind Kind => SourceKind.DirectHttp;

    /// <inheritdoc />
    public bool CanHandle(ModDefinition mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return _matches.ContainsKey(mod.Id);
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        ModDefinition mod,
        string destinationDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (!_matches.TryGetValue(mod.Id, out var match)
            || !_urlByFile.TryGetValue(match.ArchivePath, out var url))
        {
            throw new AcquisitionException(mod.Id, "no hosted archive for this mod was found in the remote pack.");
        }

        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, match.ArchivePath);

        _logger.LogInformation(
            "Downloading {Mod} from remote pack: {File} (matched on '{Key}')",
            mod.Id, match.ArchivePath, match.MatchedKey);

        try
        {
            await _downloader
                .DownloadToFileAsync(url, destination, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AcquisitionException(
                mod.Id, "the hosted archive could not be downloaded — the link may have moved.", ex);
        }

        return new DownloadResult(destination, VersionFromName(match.ArchivePath), url);
    }

    /// <summary>
    /// Pulls a version out of an archive file name when one is obviously present,
    /// e.g. "ELS V1.05.rar" → "V1.05". Best-effort, for the log only.
    /// </summary>
    private static string? VersionFromName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var match = System.Text.RegularExpressions.Regex.Match(
            name, @"[vV]?\d+(\.\d+)+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }
}
