namespace Dispatch.Core.Platform;

/// <summary>Keeps the Dispatch app itself up to date, so a new version needs no re-download.</summary>
/// <remarks>
/// The implementation lives in the composition root, where the packaging framework
/// is referenced; Core only declares the capability so the rest of the app can use
/// it without depending on that framework. Updating is always best-effort — a
/// failure leaves the current version running, never blocks startup.
/// </remarks>
public interface IAppUpdater
{
    /// <summary>
    /// Whether self-update is possible here — true only for an installed copy, not a
    /// build run from source or a portable extraction.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Checks for a newer release, downloads it, and stages it to apply the next time
    /// Dispatch is closed and reopened. Returns the version staged, or null when there
    /// is nothing to do or the check could not be completed.
    /// </summary>
    Task<string?> CheckDownloadAndStageAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used where self-update is not possible. Reports unsupported and does nothing.</summary>
public sealed class NoAppUpdater : IAppUpdater
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public Task<string?> CheckDownloadAndStageAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
