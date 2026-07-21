namespace Dispatch.Core.Platform;

/// <summary>
/// Carries the news that a new app version has been downloaded and staged, from the
/// background updater to whatever part of the UI wants to tell the user.
/// </summary>
/// <remarks>
/// The update is applied when Dispatch next closes, so nothing here interrupts a
/// session — it only lets the launcher show a quiet, dismissible "restart to update"
/// note. A single shared instance is registered so the updater and the launcher meet
/// on it without either depending on the other. Because the updater runs on a
/// background thread, the staged version is latched here too: a launcher created after
/// the update was staged still sees it, rather than only those already listening.
/// </remarks>
public sealed class AppUpdateSignal
{
    private readonly object _gate = new();
    private string? _stagedVersion;

    /// <summary>Raised when a version is staged. May fire on a background thread.</summary>
    public event EventHandler<string>? UpdateStaged;

    /// <summary>The version staged and waiting to apply on next close, or null.</summary>
    public string? StagedVersion
    {
        get
        {
            lock (_gate)
            {
                return _stagedVersion;
            }
        }
    }

    /// <summary>Records a staged version and notifies any listeners.</summary>
    public void Publish(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        lock (_gate)
        {
            _stagedVersion = version;
        }

        UpdateStaged?.Invoke(this, version);
    }
}
