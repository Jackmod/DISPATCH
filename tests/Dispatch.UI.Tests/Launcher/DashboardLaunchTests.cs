using Avalonia.Headless.XUnit;
using Dispatch.Core.Platform;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// Going on duty starts RagePluginHook, and — the point of the change — never fails
/// silently: every outcome sets a status the user can read.
/// </summary>
public sealed class DashboardLaunchTests
{
    private sealed class FakeLauncher(LaunchOutcome outcome) : IGameLauncher
    {
        public bool IsAvailable => true;
        public string? LaunchedFrom { get; private set; }

        public LaunchOutcome LaunchRagePluginHook(string gamePath)
        {
            LaunchedFrom = gamePath;
            return outcome;
        }
    }

    private static string NewGameFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dispatch-launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    [AvaloniaFact]
    public void No_game_folder_reports_it_rather_than_doing_nothing()
    {
        var model = new DashboardViewModel(launcher: new FakeLauncher(LaunchOutcome.Launched), gamePath: null);

        model.GoOnDuty();

        model.HasLaunchStatus.Should().BeTrue();
        model.LaunchStatus.Should().Contain("game folder");
    }

    [AvaloniaFact]
    public void A_successful_launch_starts_rage_from_the_game_folder_and_says_so()
    {
        var root = NewGameFolder();
        var launcher = new FakeLauncher(LaunchOutcome.Launched);

        try
        {
            var model = new DashboardViewModel(launcher: launcher, gamePath: root);

            model.GoOnDuty();

            launcher.LaunchedFrom.Should().Be(root, "RagePluginHook must be started from the game folder");
            model.LaunchStatus.Should().Contain("RagePluginHook is starting");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_dashboard_view_loads_with_the_launch_status_panel()
    {
        // Instantiating the real view loads its XAML against the app's resources,
        // so a broken resource key or binding in the launch-status panel fails here.
        var view = new DashboardView
        {
            DataContext = new DashboardViewModel(launcher: new FakeLauncher(LaunchOutcome.LoaderNotFound)),
        };

        view.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void A_missing_loader_tells_the_user_to_install_lspdfr()
    {
        var root = NewGameFolder();

        try
        {
            var model = new DashboardViewModel(launcher: new FakeLauncher(LaunchOutcome.LoaderNotFound), gamePath: root);

            model.GoOnDuty();

            model.LaunchStatus.Should().Contain("RagePluginHook.exe");
            model.LaunchStatus.Should().Contain("LSPDFR");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
