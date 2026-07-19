using Avalonia;
using Dispatch.Core.Infrastructure;
using Dispatch.Platform.Windows;
using Dispatch.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

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
        // Paths and logging come up before anything else, so that a failure
        // during composition still leaves a trace on disk to read afterwards.
        var paths = new AppPaths();
        paths.EnsureCreated();

        Log.Logger = BuildLogger(paths);

        try
        {
            Log.Information("Dispatch starting");

            using var host = BuildHost(paths);
            host.Start();

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

    private static IHost BuildHost(IAppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(paths);
        builder.Services.AddDispatchCore();
        builder.Services.AddDispatchWindows();
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
