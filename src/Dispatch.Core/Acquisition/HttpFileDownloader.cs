namespace Dispatch.Core.Acquisition;

/// <summary>
/// Streams a URL to a file, reporting progress, with a whole-file temp-then-move
/// so a partial download is never mistaken for a complete one.
/// </summary>
/// <remarks>
/// Shared by every source, so the retry, streaming and atomic-rename behaviour
/// is written once. The file is written to a <c>.partial</c> sibling and renamed
/// only after the last byte arrives; a crash or a dropped connection leaves the
/// <c>.partial</c>, never a truncated archive the extractor would choke on.
/// </remarks>
public sealed class HttpFileDownloader
{
    private readonly HttpClient _http;

    /// <summary>Constructs the downloader over an <see cref="HttpClient"/>.</summary>
    public HttpFileDownloader(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/>,
    /// overwriting any existing file there.
    /// </summary>
    public async Task DownloadToFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partial = destinationPath + ".partial";

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        var received = 0L;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(partial))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        // Rename only once the whole body is on disk. A half-file keeps the
        // .partial suffix and is never handed to the extractor.
        File.Move(partial, destinationPath, overwrite: true);
    }
}
