using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dispatch.Core.Catalogue;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// Fetches a mod from its GitHub releases, choosing the newest release's asset
/// whose name matches the catalogue pattern.
/// </summary>
/// <remarks>
/// The GitHub REST API's <c>releases/latest</c> endpoint returns the newest
/// non-prerelease release and its assets as JSON, no auth required for public
/// repos (subject to an unauthenticated rate limit, which one install run never
/// approaches). The mod's <see cref="ModDefinition.AssetPattern"/> — or, absent
/// one, a sensible default that prefers a zip — selects which asset to pull when
/// a release ships several.
///
/// <para>
/// This is the one source that can make the core of the install fully
/// hands-off: Script Hook V .NET, RAGENativeUI and LemonUI all release here.
/// </para>
/// </remarks>
public sealed class GitHubReleaseSource : IDownloadSource
{
    private readonly HttpFileDownloader _downloader;
    private readonly HttpClient _http;
    private readonly ILogger<GitHubReleaseSource> _logger;

    /// <summary>Constructs the source.</summary>
    public GitHubReleaseSource(HttpFileDownloader downloader, HttpClient http, ILogger<GitHubReleaseSource> logger)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        _downloader = downloader;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public SourceKind Kind => SourceKind.GitHubRelease;

    /// <inheritdoc />
    public bool CanHandle(ModDefinition mod) =>
        mod.Source == SourceKind.GitHubRelease && !string.IsNullOrWhiteSpace(mod.Repo);

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
            throw new AcquisitionException(mod.Id, "no GitHub repository is configured for this mod.");
        }

        var release = await FetchLatestReleaseAsync(mod, cancellationToken).ConfigureAwait(false);
        var asset = SelectAsset(mod, release);

        if (asset is null)
        {
            throw new AcquisitionException(
                mod.Id,
                $"the latest release of {mod.Repo} had no downloadable asset matching this mod. "
                + "The release layout may have changed.");
        }

        var destination = Path.Combine(destinationDir, asset.Name);
        _logger.LogInformation("Fetching {Mod} {Version} asset {Asset}", mod.Id, release.TagName, asset.Name);

        await _downloader
            .DownloadToFileAsync(asset.DownloadUrl, destination, progress, cancellationToken)
            .ConfigureAwait(false);

        return new DownloadResult(destination, release.TagName, asset.DownloadUrl);
    }

    private async Task<Release> FetchLatestReleaseAsync(ModDefinition mod, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{mod.Repo}/releases/latest";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AcquisitionException(mod.Id, "GitHub could not be reached. Check the network connection.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new AcquisitionException(
                    mod.Id, $"{mod.Repo} has no published releases, or the repository was renamed.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new AcquisitionException(
                    mod.Id, "GitHub is rate limiting anonymous requests. Try again in a few minutes.");
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = ParseRelease(json);

            return release
                   ?? throw new AcquisitionException(mod.Id, "GitHub returned a release in a shape Dispatch did not understand.");
        }
    }

    /// <summary>
    /// Picks the asset to download: the first whose name matches the mod's
    /// pattern, or — when the mod names no pattern — the newest zip, falling back
    /// to any single asset.
    /// </summary>
    internal static Asset? SelectAsset(ModDefinition mod, Release release)
    {
        if (release.Assets.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(mod.AssetPattern))
        {
            var pattern = new Regex(mod.AssetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var matched = release.Assets.FirstOrDefault(a => pattern.IsMatch(a.Name));
            if (matched is not null)
            {
                return matched;
            }
        }

        // No pattern, or none matched: prefer an archive over a bare installer or
        // a source tarball, and a zip over everything since that is what the BCL
        // reads without a package.
        return release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? release.Assets.FirstOrDefault(a => IsArchive(a.Name))
               ?? release.Assets[0];
    }

    private static bool IsArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

    internal static Release? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;

        var assets = new List<Asset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in assetsElement.EnumerateArray())
            {
                var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
                var download = element.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                var size = element.TryGetProperty("size", out var s) && s.TryGetInt64(out var parsed) ? parsed : 0L;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(download))
                {
                    assets.Add(new Asset(name, download, size));
                }
            }
        }

        return new Release(tag, assets);
    }

    /// <summary>A GitHub release, reduced to what the source needs.</summary>
    internal sealed record Release(string TagName, IReadOnlyList<Asset> Assets);

    /// <summary>One downloadable asset on a release.</summary>
    internal sealed record Asset(string Name, string DownloadUrl, long Size);
}
