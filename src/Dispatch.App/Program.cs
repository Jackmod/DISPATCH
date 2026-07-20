using Avalonia;
using Dispatch.Core.Infrastructure;
using Dispatch.Platform.Windows;
using Dispatch.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Velopack;

namespace Dispatch.App;

/// <summary>
/// The composition root. The only place in the application that knows about
/// Core, UI and the Windows platform layer at the same time.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack first, before anything else runs. During an install, update or
        // uninstall the launcher re-invokes the executable with hidden hook
        // arguments; this handles them and exits, so those hooks never touch the
        // app's real startup. On a normal launch it does nothing and returns.
        VelopackApp.Build().Run();

        // Dry-run mode: walk the whole UI without downloading a mod or writing a
        // file to the game. The simulated runner just plays the phases.
        var demo = args.Any(a => a is "--demo" or "--dry-run")
                   || Environment.GetEnvironmentVariable("DISPATCH_DEMO") is "1" or "true";

        // Offline mode: a REAL install that never downloads — every mod comes from
        // the bundled pack. For testing the full install/config on a machine where
        // the pack is already present, without any network round-trip.
        var offline = args.Any(a => a is "--offline" or "--no-download")
                      || Environment.GetEnvironmentVariable("DISPATCH_OFFLINE") is "1" or "true";

        // Paths and logging come up before anything else, so that a failure
        // during composition still leaves a trace on disk to read afterwards. In
        // demo, every path is redirected to a throwaway folder so nothing real is
        // touched — no install record, so no config is ever written to the game.
        var paths = demo
            ? new AppPaths(
                Path.Combine(Path.GetTempPath(), "Dispatch-Demo"),
                Path.Combine(Path.GetTempPath(), "Dispatch-Demo-temp"))
            : new AppPaths();
        paths.EnsureCreated();

        Log.Logger = BuildLogger(paths);

        try
        {
            Log.Information(
                demo ? "Dispatch starting in DEMO mode (no downloads, no writes)"
                : offline ? "Dispatch starting in OFFLINE mode (real install, pack only, no downloads)"
                : "Dispatch starting");

            using var host = BuildHost(paths, demo, offline);
            host.Start();

            // Two background best-effort tasks, so the one installer keeps working
            // as things change: refresh the hosted-pack manifest (new/renamed mods
            // reach this copy), and check for a newer app version (staged to apply
            // when Dispatch next closes). Both swallow every failure; demo does neither.
            if (!demo)
            {
                _ = host.Services
                    .GetRequiredService<Dispatch.Core.Acquisition.RemotePackRefresher>()
                    .RefreshAsync();

                _ = host.Services
                    .GetRequiredService<Dispatch.Core.Platform.IAppUpdater>()
                    .CheckDownloadAndStageAsync();
            }

            var exitCode = BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);

            host.StopAsync().GetAwaiter().GetResult();

            Log.Information("Dispatch exited with code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            // Nothing here reaches the user as a stack trace; the error
            // catalogue owns user-facing wording once it exists. This is the
            // last-resort net for a failure before the UI is even up.
            Log.Fatal(ex, "Dispatch terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Used by the Avalonia XAML previewer and by <see cref="Main"/>.</summary>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new UI.App(services))
            .UsePlatformDetect()
            .LogToTrace();

    private static IHost BuildHost(IAppPaths paths, bool demo, bool offline)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(paths);

        // Registered before AddDispatchCore so its TryAdd keeps these overrides.
        if (demo)
        {
            // Simulated runner: plays the phases against a clock, downloads and
            // writes nothing.
            builder.Services.AddSingleton<Dispatch.Core.Installation.IInstallRunner>(
                _ => new Dispatch.Core.Installation.SimulatedInstallRunner(TimeSpan.FromMilliseconds(140)));
        }

        if (offline)
        {
            // Real install, but every mod is sourced from the bundled pack.
            builder.Services.AddSingleton(new Dispatch.Core.Acquisition.AcquisitionOptions(Offline: true));
        }

        // The real self-updater, overriding Core's no-op default. Registered here
        // because only the composition root references the packaging framework; on a
        // dev build it reports unsupported and does nothing.
        builder.Services.AddSingleton<Dispatch.Core.Platform.IAppUpdater, VelopackAppUpdater>();

        // Platform layer before Core, for the same reason as the overrides above:
        // Core registers silent audio fallbacks with TryAdd, so the real winmm
        // sound player and SAPI voice must be registered first or the fallbacks
        // win and neither the intro siren nor the callsign read-back ever plays.
        builder.Services.AddDispatchWindows();
        builder.Services.AddDispatchCore();
        builder.Services.AddDispatchUi();

        return builder.Build();
    }

    private static Serilog.ILogger BuildLogger(IAppPaths paths) =>
        new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "dispatch-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
}
