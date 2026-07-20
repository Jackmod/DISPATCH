using System.IO.Compression;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Dispatch.Core.Acquisition;

/// <summary>The archive formats Dispatch can unpack.</summary>
public enum ArchiveFormat
{
    /// <summary>A zip, handled by the BCL.</summary>
    Zip,

    /// <summary>A 7-Zip archive, handled by SharpCompress.</summary>
    SevenZip,

    /// <summary>A RAR archive, handled by SharpCompress.</summary>
    Rar,

    /// <summary>A file that is not an archive at all — a bare .dll or .asi.</summary>
    Loose,
}

/// <summary>Unpacks a downloaded archive into a staging folder.</summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Extracts <paramref name="archivePath"/> into <paramref name="destinationDir"/>,
    /// which must already exist and be empty.
    /// </summary>
    /// <returns>The number of files written.</returns>
    /// <exception cref="UnsafeArchivePathException">A member escaped the destination.</exception>
    Task<int> ExtractAsync(string archivePath, string destinationDir, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unpacks zip, 7z and rar archives into staging, refusing any member that would
/// escape the target directory.
/// </summary>
/// <remarks>
/// Zip goes through <see cref="ZipArchive"/> — it is in the BCL, streams, and is
/// the format every GitHub release asset here ships as. 7z and rar go through
/// SharpCompress, which most LSPDFR plugins need. A file that is not an archive
/// (a bare <c>.dll</c> or <c>.asi</c> served directly) is copied through as a
/// single loose file, so the caller need not special-case it.
///
/// <para>
/// Every destination path is resolved through
/// <see cref="StagingArea.ResolveWithin"/>, the one place path-traversal is
/// defended, so a malicious <c>../../</c> member cannot write outside staging no
/// matter which format delivered it.
/// </para>
/// </remarks>
public sealed class ArchiveExtractor : IArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;

    /// <summary>Constructs the extractor.</summary>
    public ArchiveExtractor(ILogger<ArchiveExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ExtractAsync(
        string archivePath, string destinationDir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDir);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Archive to extract was not found.", archivePath);
        }

        Directory.CreateDirectory(destinationDir);

        var format = DetectFormat(archivePath);
        _logger.LogInformation("Extracting {Archive} as {Format}", Path.GetFileName(archivePath), format);

        return format switch
        {
            ArchiveFormat.Zip => await ExtractZipAsync(archivePath, destinationDir, cancellationToken).ConfigureAwait(false),
            ArchiveFormat.Loose => await CopyLooseAsync(archivePath, destinationDir, cancellationToken).ConfigureAwait(false),
            _ => ExtractWithSharpCompress(archivePath, destinationDir, cancellationToken),
        };
    }

    /// <summary>
    /// Determines the format from the file signature first, extension second.
    /// </summary>
    /// <remarks>
    /// Signature before extension because a mod host often serves a <c>.zip</c>
    /// that is really a 7z, and trusting the extension would hand it to the wrong
    /// reader. The magic bytes do not lie.
    /// </remarks>
    public static ArchiveFormat DetectFormat(string archivePath)
    {
        Span<byte> head = stackalloc byte[6];
        int read;
        using (var stream = File.OpenRead(archivePath))
        {
            read = stream.Read(head);
        }

        if (read >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
        {
            return ArchiveFormat.Zip;
        }

        // "7z\xBC\xAF\x27\x1C"
        if (read >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC &&
            head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C)
        {
            return ArchiveFormat.SevenZip;
        }

        // "Rar!" — both RAR4 (\x1A\x07\x00) and RAR5 (\x1A\x07\x01\x00) start this way.
        if (read >= 4 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21)
        {
            return ArchiveFormat.Rar;
        }

        // No archive signature — fall back to the extension, and treat anything
        // unrecognised as a single loose file to be copied through.
        return Path.GetExtension(archivePath).ToLowerInvariant() switch
        {
            ".zip" => ArchiveFormat.Zip,
            ".7z" => ArchiveFormat.SevenZip,
            ".rar" => ArchiveFormat.Rar,
            _ => ArchiveFormat.Loose,
        };
    }

    private async Task<int> ExtractZipAsync(
        string archivePath, string destinationDir, CancellationToken cancellationToken)
    {
        var written = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A directory entry (trailing slash, zero length) carries no data;
            // the files inside it create their own parent directories.
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            var target = StagingArea.ResolveWithin(destinationDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using var source = entry.Open();
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            written++;
        }

        return written;
    }

    private int ExtractWithSharpCompress(
        string archivePath, string destinationDir, CancellationToken cancellationToken)
    {
        var written = 0;

        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            var target = StagingArea.ResolveWithin(destinationDir, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            entry.WriteToFile(target, new ExtractionOptions { Overwrite = true });
            written++;
        }

        return written;
    }

    private static async Task<int> CopyLooseAsync(
        string archivePath, string destinationDir, CancellationToken cancellationToken)
    {
        var target = Path.Combine(destinationDir, Path.GetFileName(archivePath));

        await using var source = File.OpenRead(archivePath);
        await using var destination = File.Create(target);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        return 1;
    }
}
