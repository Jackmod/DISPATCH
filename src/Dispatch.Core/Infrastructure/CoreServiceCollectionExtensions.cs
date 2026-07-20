using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dispatch.Core.Infrastructure;

/// <summary>
/// Registers the domain services. The composition root calls this; nothing
/// else in Core knows the container exists.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Adds every Dispatch.Core service to <paramref name="services"/>.</summary>
    public static IServiceCollection AddDispatchCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd throughout: the composition root may have already supplied a
        // rooted IAppPaths, and a test host certainly will.
        services.TryAddSingleton<IAppPaths, AppPaths>();
        services.TryAddSingleton<Profiles.IProfileStore, Profiles.ProfileStore>();
        services.TryAddSingleton<Profiles.IInstallRecordStore, Profiles.InstallRecordStore>();
        services.TryAddSingleton<Detection.IGameBuildWatch, Detection.GameBuildWatch>();
        services.TryAddSingleton<Imagery.IUserBackgrounds, Imagery.UserBackgrounds>();

        AddAcquisition(services);

        // The real, file-writing runner is the default now the acquisition and
        // placement layers exist. A test host that wants the dry-run simulation
        // registers SimulatedInstallRunner before calling this and TryAdd keeps it.
        services.TryAddSingleton<Configuration.IniConfigWriter>();
        services.TryAddSingleton<Configuration.ConfigInstaller>();

        services.TryAddSingleton<Installation.FilePlacer>();
        services.TryAddSingleton<Installation.LocalInstallRunner>();
        services.TryAddSingleton<Installation.IRunIdFactory, Installation.RunIdFactory>();
        services.TryAddSingleton<Installation.IInstallRunner, Installation.RealInstallRunner>();

        // Speech is Windows-only; the platform project overrides this with the
        // real implementation. Registered here so Core alone is still usable.
        services.TryAddSingleton<Audio.ICallsignVoice, Audio.SilentCallsignVoice>();
        services.TryAddSingleton<Audio.ISoundPlayer, Audio.SilentSoundPlayer>();
        services.TryAddSingleton<Detection.IFileSystemProbe, Detection.RealFileSystemProbe>();
        services.TryAddSingleton<Detection.IGameLocator, Detection.GameLocator>();
        services.TryAddSingleton<Detection.IVersionReader, Detection.VersionReader>();
        services.TryAddSingleton<Detection.IGameProcessGuard, Detection.GameProcessGuard>();

        // Platform services default to no-ops; the Windows project or the composition
        // root overrides them (the app updater needs the packaging framework, which
        // only the composition root references).
        services.TryAddSingleton<Platform.IDefenderService, Platform.NoDefenderService>();
        services.TryAddSingleton<Platform.IGameLauncher, Platform.NoGameLauncher>();
        services.TryAddSingleton<Platform.IAppUpdater, Platform.NoAppUpdater>();

        services.TryAddSingleton<Maintenance.IQuarantine>(sp =>
            new Maintenance.Quarantine(
                sp.GetRequiredService<IAppPaths>().QuarantineDirectory,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Maintenance.Quarantine>>()));

        services.TryAddSingleton<Maintenance.IBackupStore>(sp =>
            new Maintenance.BackupStore(
                sp.GetRequiredService<IAppPaths>().BackupsDirectory,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Maintenance.BackupStore>>()));

        return services;
    }

    /// <summary>
    /// Registers the acquisition graph: the shared HTTP client, the downloader,
    /// the archive extractor, every automated source, and the acquirer that picks
    /// between them.
    /// </summary>
    private static void AddAcquisition(IServiceCollection services)
    {
        // One long-lived client for the whole app. GitHub's API rejects requests
        // with no User-Agent, and the ten-minute timeout covers a large archive
        // on a slow line. A desktop app with a single client does not need the
        // factory machinery a server would.
        services.TryAddSingleton(_ =>
        {
            var client = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Dispatch/1.0 (+https://github.com/Jackmod/DISPATCH)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        });

        services.TryAddSingleton<Acquisition.HttpFileDownloader>();
        services.TryAddSingleton<Acquisition.IArchiveExtractor, Acquisition.ArchiveExtractor>();

        // The pack roots as an injectable options value, computed from paths.
        services.TryAddSingleton(sp => new Acquisition.ModPackRoots(ModPackRoots(sp.GetRequiredService<IAppPaths>())));

        // The hosted pack index, loaded once from the small remote-pack.json that
        // ships beside the executable. Absent (or empty) in a fat build that bundles
        // the whole pack; populated in a thin build that downloads on demand.
        services.TryAddSingleton(sp => LoadRemotePackIndex(sp.GetRequiredService<IAppPaths>()));
        services.TryAddSingleton<Acquisition.RemotePackRefresher>();

        // TryAddEnumerable so the acquirer receives every source exactly once,
        // even if AddDispatchCore is called more than once in a host. Order is
        // preserved and the acquirer takes the first source that can handle a mod,
        // so the bundled pack is registered first: a mod present in the pack
        // installs from it. The hosted remote pack comes next, so a thin install
        // fetches on demand what the local pack does not have; anything neither
        // holds falls through to the per-mod network sources.
        // Each is a concrete implementation type so TryAddEnumerable can tell them
        // apart.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Acquisition.IDownloadSource, Acquisition.BundledModSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Acquisition.IDownloadSource, Acquisition.RemotePackSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Acquisition.IDownloadSource, Acquisition.GitHubReleaseSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Acquisition.IDownloadSource, Acquisition.DirectHttpSource>());

        // Default to online; the composition root overrides this with an offline
        // options value when --offline is set.
        services.TryAddSingleton(new Acquisition.AcquisitionOptions());
        services.TryAddSingleton<Acquisition.Acquirer>();
    }

    /// <summary>
    /// The mod pack roots, most specific first: the user's writable pack under
    /// LOCALAPPDATA, then the pack shipped beside the executable. The first that
    /// holds a mod wins, so a user drop overrides the shipped copy.
    /// </summary>
    private static IReadOnlyList<string> ModPackRoots(IAppPaths paths) =>
    [
        paths.ModPackDirectory,
        Path.Combine(AppContext.BaseDirectory, "modpack"),
    ];

    /// <summary>
    /// Loads the hosted pack index, preferring the refreshed cache under the app's
    /// data folder (where <see cref="Acquisition.RemotePackRefresher"/> writes the
    /// live manifest) over the copy shipped beside the executable. A missing, empty
    /// or malformed file falls through to the next candidate; none yields an empty
    /// index, which is exactly what a fat build wants.
    /// </summary>
    private static Acquisition.RemotePackIndex LoadRemotePackIndex(IAppPaths paths)
    {
        string[] candidates =
        [
            Path.Combine(paths.Root, "remote-pack.json"),
            Path.Combine(AppContext.BaseDirectory, "remote-pack.json"),
        ];

        foreach (var path in candidates)
        {
            var index = TryReadRemotePackIndex(path);
            if (index.Entries.Count > 0)
            {
                return index;
            }
        }

        return new Acquisition.RemotePackIndex([]);
    }

    private static Acquisition.RemotePackIndex TryReadRemotePackIndex(string path)
    {
        if (!File.Exists(path))
        {
            return new Acquisition.RemotePackIndex([]);
        }

        try
        {
            var json = File.ReadAllText(path);
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<Acquisition.RemotePackEntry>>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new Acquisition.RemotePackIndex(entries ?? []);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return new Acquisition.RemotePackIndex([]);
        }
    }
}
