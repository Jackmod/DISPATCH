using Dispatch.Core.Platform;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Dispatch.App;

/// <summary>
/// Keeps Dispatch up to date from its GitHub releases, so a new app version applies
/// itself instead of needing the installer downloaded again.
/// </summary>
/// <remarks>
/// This lives in the composition root because it is the only project that references
/// Velopack. It reads the public DISPATCH releases (no token needed), downloads a
/// newer full release, and stages it to apply when Dispatch is next closed — so the
/// user is never interrupted mid-session and simply opens the new version next time.
/// Everything is guarded: on a copy that was not installed (a dev build), or on any
/// network or packaging error, it does nothing and the current version keeps running.
/// </remarks>
public sealed class VelopackAppUpdater : IAppUpdater
{
    // The public repo whose releases carry the Velopack update feed (RELEASES + the
    // full .nupkg alongside the Setup.exe).
    private const string ReleasesRepoUrl = "https://github.com/Jackmod/DISPATCH";

    private readonly UpdateManager _manager;
    private readonly ILogger<VelopackAppUpdater> _logger;

    /// <summary>Constructs the updater over the GitHub releases feed.</summary>
    public VelopackAppUpdater(ILogger<VelopackAppUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // No access token: the releases are public. Prereleases are ignored so a
        // draft or beta never auto-installs.
        _manager = new UpdateManager(new GithubSource(ReleasesRepoUrl, accessToken: null, prerelease: false));
    }

    /// <inheritdoc />
    public bool IsSupported => _manager.IsInstalled;

    /// <inheritdoc />
    public async Task<string?> CheckDownloadAndStageAsync(CancellationToken cancellationToken = default)
    {
        // A build run from source or a portable copy cannot update itself; that is
        // expected, not an error.
        if (!_manager.IsInstalled)
        {
            return null;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return null;
            }

            await _manager.DownloadUpdatesAsync(update, progress: null, cancellationToken).ConfigureAwait(false);

            // Apply on exit, without forcing a restart now: the update is live the
            // next time Dispatch opens, so an in-progress session is never disturbed.
            _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);

            var version = update.TargetFullRelease.Version.ToString();
            _logger.LogInformation("Staged app update to {Version}; it applies when Dispatch next closes", version);
            return version;
        }
        catch (Exception ex)
        {
            // Updating must never take the app down; a failure just leaves the
            // current version running.
            _logger.LogInformation(ex, "App update check failed; staying on the current version");
            return null;
        }
    }
}
