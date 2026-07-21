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

    // A folder that passes the launch pre-flight: the three core components present.
    private static string ReadyGameFolder()
    {
        var root = NewGameFolder();
        File.WriteAllText(Path.Combine(root, "RagePluginHook.exe"), "x");
        File.WriteAllText(Path.Combine(root, "ScriptHookV.dll"), "x");
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        File.WriteAllText(Path.Combine(root, "plugins", "LSPD First Response.dll"), "x");
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
        // A ready folder clears the pre-flight, so the launch actually goes.
        var root = ReadyGameFolder();
        var launcher = new FakeLauncher(LaunchOutcome.Launched);

        try
        {
            var model = new DashboardViewModel(launcher: launcher, gamePath: root);

            model.GoOnDuty();

            model.ShowPreLaunchWarning.Should().BeFalse("every core component is present");
            launcher.LaunchedFrom.Should().Be(root, "RagePluginHook must be started from the game folder");
            model.LaunchStatus.Should().Contain("RagePluginHook is starting");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Pre_flight_catches_a_missing_component_before_launching()
    {
        // An empty folder is missing everything; the pre-flight must stop the launch
        // and explain, rather than start the game into a black screen.
        var root = NewGameFolder();
        var launcher = new FakeLauncher(LaunchOutcome.Launched);

        try
        {
            var model = new DashboardViewModel(launcher: launcher, gamePath: root);

            model.GoOnDuty();

            model.ShowPreLaunchWarning.Should().BeTrue();
            model.PreLaunchIssues.Should().Contain(i => i.Contains("RagePluginHook"));
            launcher.LaunchedFrom.Should().BeNull("the launch was blocked by pre-flight");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Launch_anyway_overrides_the_pre_flight_and_launches()
    {
        var root = NewGameFolder();
        var launcher = new FakeLauncher(LaunchOutcome.LoaderNotFound);

        try
        {
            var model = new DashboardViewModel(launcher: launcher, gamePath: root);

            model.GoOnDuty();
            model.ShowPreLaunchWarning.Should().BeTrue();

            model.LaunchAnywayCommand.Execute(null);

            model.ShowPreLaunchWarning.Should().BeFalse();
            launcher.LaunchedFrom.Should().Be(root, "the override launches despite the warning");
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
        // Pre-flight passes (a ready folder), but the loader still can't start — the
        // outcome message must name the loader and LSPDFR.
        var root = ReadyGameFolder();

        try
        {
            var model = new DashboardViewModel(launcher: new FakeLauncher(LaunchOutcome.LoaderNotFound), gamePath: root);

            model.GoOnDuty();

            model.ShowPreLaunchWarning.Should().BeFalse();
            model.LaunchStatus.Should().Contain("RagePluginHook.exe");
            model.LaunchStatus.Should().Contain("LSPDFR");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
