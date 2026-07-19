using Dispatch.Core.Installation;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Installation;

/// <summary>
/// The simulation stands in for the real runner on the install screen, so the
/// contract it teaches that screen has to be the contract the real one will
/// keep: phases only move forward, the counter never exceeds the total, and
/// cancellation stops promptly.
/// </summary>
public sealed class SimulatedInstallRunnerTests
{
    // A tick short enough to keep the suite fast, long enough that
    // cancellation has something to interrupt.
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private static InstallRequest Request(int mods = 41) =>
        new(GamePath: @"C:\Games\GTAV", PresetName: "Full Duty", ModCount: mods);

    private static (SimulatedInstallRunner Runner, List<InstallProgress> Updates, Progress<InstallProgress> Progress) Arrange()
    {
        var updates = new List<InstallProgress>();

        // Progress<T> posts to the captured context; there is none in a test,
        // so the callback runs inline on the thread pool. Collecting under a
        // lock keeps that safe.
        var gate = new object();
        var progress = new Progress<InstallProgress>(update =>
        {
            lock (gate)
            {
                updates.Add(update);
            }
        });

        return (new SimulatedInstallRunner(Tick), updates, progress);
    }

    [Fact]
    public async Task Run_reports_every_phase_in_order()
    {
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(), progress);

        var phases = updates.Select(u => u.Phase).Distinct().ToList();

        phases.Should().Equal(
            InstallPhase.Collecting,
            InstallPhase.CheckingCompatibility,
            InstallPhase.BackingUp,
            InstallPhase.PlacingFiles,
            InstallPhase.WritingConfiguration,
            InstallPhase.InstallingTextures,
            InstallPhase.Verifying);
    }

    [Fact]
    public async Task Phase_index_never_goes_backwards()
    {
        // The progress rail fills left to right and never unfills. A phase
        // index that regressed would make completed segments go dark.
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(), progress);

        updates.Select(u => u.PhaseIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Counter_never_exceeds_the_total()
    {
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(mods: 12), progress);

        updates.Should().OnlyContain(u => u.Completed <= u.Total);
        updates.Should().OnlyContain(u => u.Total == 12);
    }

    [Fact]
    public async Task Counter_only_advances()
    {
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(), progress);

        updates.Select(u => u.Completed).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Every_update_carries_a_detail_line()
    {
        // The mono line is never blank mid-run; an empty detail reads as a
        // stall even while the counter moves.
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(), progress);

        updates.Should().OnlyContain(u => !string.IsNullOrWhiteSpace(u.Detail));
    }

    [Fact]
    public async Task Report_names_what_needs_attention()
    {
        var (runner, _, progress) = Arrange();

        var report = await runner.RunAsync(Request(), progress);

        report.NeedsAttention.Should().NotBeEmpty("the simulation models a realistic run");
        report.NeedsAttention.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Reason));
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public async Task A_mod_that_needs_attention_is_not_also_reported_as_installed()
    {
        var (runner, _, progress) = Arrange();

        var report = await runner.RunAsync(Request(), progress);

        foreach (var problem in report.NeedsAttention)
        {
            report.Installed.Should().NotContain(problem.Mod);
        }
    }

    [Fact]
    public async Task Cancellation_stops_the_run()
    {
        var runner = new SimulatedInstallRunner(TimeSpan.FromMilliseconds(20));
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(_ => cancellation.Cancel());

        var run = async () => await runner.RunAsync(Request(), progress, cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_already_cancelled_token_starts_nothing()
    {
        var (runner, updates, progress) = Arrange();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var run = async () => await runner.RunAsync(Request(), progress, cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Phase_names_are_the_ones_the_screen_shows()
    {
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(), progress);

        updates.Select(u => u.PhaseName).Distinct().Should().Equal(
            "Collecting files",
            "Checking compatibility",
            "Backing up",
            "Placing files",
            "Writing configuration",
            "Installing textures",
            "Verifying");
    }

    [Fact]
    public async Task Elapsed_time_is_measured_rather_than_left_at_zero()
    {
        var (runner, _, progress) = Arrange();

        var report = await runner.RunAsync(Request(), progress);

        report.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(41)]
    public async Task Any_preset_size_completes(int mods)
    {
        var (runner, updates, progress) = Arrange();

        await runner.RunAsync(Request(mods), progress);

        updates.Should().NotBeEmpty();
        updates[^1].Phase.Should().Be(InstallPhase.Verifying);
    }
}
