using Dispatch.Core.Catalogue;

namespace Dispatch.Core.Acquisition;

/// <summary>Options that shape an acquisition run.</summary>
/// <param name="Offline">
/// When true, only the bundled pack is used — no network source is consulted, so
/// a real install proceeds entirely from local files with no download.
/// </param>
public sealed record AcquisitionOptions(bool Offline = false);

/// <summary>Progress of a single download, 0 to 1 where the length is known.</summary>
/// <param name="BytesReceived">Bytes downloaded so far.</param>
/// <param name="TotalBytes">Total to download, or null when the server did not say.</param>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Fraction complete, or null when the total is unknown.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>What a completed download produced.</summary>
/// <param name="ArchivePath">Where the fetched file landed on disk.</param>
/// <param name="ResolvedVersion">The version fetched, when the source could name it.</param>
/// <param name="SourceUrl">The exact URL the bytes came from, for the log.</param>
public sealed record DownloadResult(string ArchivePath, string? ResolvedVersion, string SourceUrl);

/// <summary>
/// A way to fetch a mod's archive without user interaction.
/// </summary>
/// <remarks>
/// One implementation per <see cref="SourceKind"/> that can be automated. The
/// browser kind has no source here — it needs a human at a login page, so it is
/// handled outside this contract. The acquirer picks the first source that
/// reports it can handle a mod.
/// </remarks>
public interface IDownloadSource
{
    /// <summary>The source kind this handles.</summary>
    SourceKind Kind { get; }

    /// <summary>Whether this source can fetch the given mod as configured.</summary>
    bool CanHandle(ModDefinition mod);

    /// <summary>
    /// Downloads the mod's archive into <paramref name="destinationDir"/>.
    /// </summary>
    /// <exception cref="AcquisitionException">The download could not be completed.</exception>
    Task<DownloadResult> DownloadAsync(
        ModDefinition mod,
        string destinationDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a mod's archive cannot be fetched. Carries a reason fit to show a user.</summary>
public sealed class AcquisitionException(string modId, string reason, Exception? inner = null)
    : Exception($"Could not fetch '{modId}': {reason}", inner)
{
    /// <summary>The mod that failed.</summary>
    public string ModId { get; } = modId;

    /// <summary>The plain-language reason, written for someone reading a report.</summary>
    public string Reason { get; } = reason;
}
