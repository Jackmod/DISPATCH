using Dispatch.Core.Catalogue;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// Fetches a mod from a fixed download URL.
/// </summary>
/// <remarks>
/// For mods served from a stable static link — a versionless "latest" URL on the
/// author's own host. The file name is taken from the URL, or a fallback built
/// from the mod id when the URL ends in a path with no name.
/// </remarks>
public sealed class DirectHttpSource : IDownloadSource
{
    private readonly HttpFileDownloader _downloader;
    private readonly ILogger<DirectHttpSource> _logger;

    /// <summary>Constructs the source.</summary>
    public DirectHttpSource(HttpFileDownloader downloader, ILogger<DirectHttpSource> logger)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(logger);

        _downloader = downloader;
        _logger = logger;
    }

    /// <inheritdoc />
    public SourceKind Kind => SourceKind.DirectHttp;

    /// <inheritdoc />
    public bool CanHandle(ModDefinition mod) =>
        mod.Source == SourceKind.DirectHttp
        && Uri.TryCreate(mod.Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        ModDefinition mod,
        string destinationDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (!CanHandle(mod))
        {
            throw new AcquisitionException(mod.Id, "no valid download URL is configured for this mod.");
        }

        var fileName = FileNameFor(mod);
        var destination = Path.Combine(destinationDir, fileName);
        _logger.LogInformation("Fetching {Mod} from {Url}", mod.Id, mod.Url);

        try
        {
            await _downloader
                .DownloadToFileAsync(mod.Url, destination, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AcquisitionException(
                mod.Id, "the download host returned an error. The link may have moved.", ex);
        }

        return new DownloadResult(destination, null, mod.Url);
    }

    private static string FileNameFor(ModDefinition mod)
    {
        var uri = new Uri(mod.Url, UriKind.Absolute);
        var last = uri.Segments.LastOrDefault()?.Trim('/');

        return string.IsNullOrWhiteSpace(last) ? $"{mod.Id}.zip" : Uri.UnescapeDataString(last);
    }
}
