using System.Text.Json;
using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Acquisition;

/// <summary>
/// Refreshes the hosted-pack index from its live URL, so an already-installed thin
/// installer picks up mods added or renamed since it was built — without being
/// rebuilt or redistributed.
/// </summary>
/// <remarks>
/// The <c>remote-pack.json</c> shipped beside the executable is the offline floor.
/// This fetches the current one from the same release that serves the mods and
/// writes it to a cache the loader prefers on the next launch. Every failure is
/// swallowed: a missing network, a 404, a slow host or a malformed body just leaves
/// the last good manifest in place, so a refresh can only ever improve things and
/// never break an install.
/// </remarks>
public sealed class RemotePackRefresher
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    // A UTF-8 BOM, which the manifest may be written with; GitHub serves it
    // byte-for-byte and the JSON reader rejects a leading one, so it is stripped
    // before parsing.
    private const char Bom = '﻿';

    private readonly HttpClient _http;
    private readonly IAppPaths _paths;
    private readonly AcquisitionOptions _options;
    private readonly ILogger<RemotePackRefresher> _logger;

    /// <summary>Constructs the refresher.</summary>
    public RemotePackRefresher(
        HttpClient http, IAppPaths paths, AcquisitionOptions options, ILogger<RemotePackRefresher> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _paths = paths;
        _options = options;
        _logger = logger;
    }

    /// <summary>The cache file the index loader reads before the bundled copy.</summary>
    public string CachePath => Path.Combine(_paths.Root, "remote-pack.json");

    /// <summary>
    /// Fetches the live manifest and, if it is valid and non-empty, writes it to the
    /// cache. Best-effort: any failure is logged and swallowed.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Offline || string.IsNullOrWhiteSpace(_options.ManifestUrl))
        {
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(FetchTimeout);

            var raw = await _http.GetStringAsync(_options.ManifestUrl, cts.Token).ConfigureAwait(false);
            var json = raw.TrimStart(Bom);

            // Only replace the cache with something that actually parses as a
            // non-empty list of file+url pairs; a rate-limit HTML page or an empty
            // body must never overwrite a working manifest.
            var entries = JsonSerializer.Deserialize<List<RemotePackEntry>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is null || entries.Count == 0
                || entries.Any(e => string.IsNullOrWhiteSpace(e.File) || string.IsNullOrWhiteSpace(e.Url)))
            {
                _logger.LogInformation(
                    "Remote pack manifest at {Url} was empty or malformed; keeping the current one", _options.ManifestUrl);
                return;
            }

            _paths.EnsureCreated();
            var temp = CachePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, CachePath, overwrite: true);

            _logger.LogInformation(
                "Refreshed remote pack manifest: {Count} entrie(s) from {Url}", entries.Count, _options.ManifestUrl);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or OperationCanceledException
               or IOException or JsonException or NotSupportedException)
        {
            _logger.LogInformation(ex, "Could not refresh the remote pack manifest; keeping the current one");
        }
    }
}
