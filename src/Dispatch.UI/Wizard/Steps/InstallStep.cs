using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Detection;
using Dispatch.Core.Installation;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>
/// Screen 5. The screen goes quiet: phase, detail line, counter, and the
/// lightbar rail as the progress indicator.
/// </summary>
/// <remarks>
/// The step owns no install logic. It starts the runner, forwards progress to
/// bindable properties, and asks the wizard to advance when the run finishes.
/// Swapping the simulated runner for the real one changes nothing here.
/// </remarks>
public sealed partial class InstallStep : WizardStep
{
    private readonly IInstallRunner _runner;
    private readonly Core.Detection.IGameProcessGuard? _guard;
    private CancellationTokenSource? _cancellation;
    private bool _hasRun;

    // The wall clock for the run, and a one-second timer that turns it into the
    // elapsed and estimated-remaining readouts.
    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _ticker;

    /// <summary>True when the install is held back because the game is running.</summary>
    [ObservableProperty]
    private bool _blockedByGame;

    /// <summary>The running process holding the folder open, when blocked.</summary>
    [ObservableProperty]
    private string _blockingProcess = string.Empty;

    [ObservableProperty]
    private int _phaseIndex;

    [ObservableProperty]
    private string _phase = "Collecting files";

    [ObservableProperty]
    private string _detail = "Preparing";

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private int _total = 41;

    [ObservableProperty]
    private bool _isLogExpanded;

    [ObservableProperty]
    private double _fraction;

    [ObservableProperty]
    private int _percent;

    /// <summary>Elapsed time since the run started, as "m:ss".</summary>
    [ObservableProperty]
    private string _elapsedText = "0:00";

    /// <summary>Estimated time remaining, as "~m:ss left" or a placeholder.</summary>
    [ObservableProperty]
    private string _remainingText = "estimating…";

    /// <summary>The game edition being installed onto. Legacy is the only supported one.</summary>
    public GameEdition Edition { get; set; } = GameEdition.Legacy;

    /// <summary>Constructs the screen against a runner.</summary>
    public InstallStep(IInstallRunner runner, Core.Detection.IGameProcessGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _guard = guard;
    }

    /// <summary>
    /// What to install, set by the wizard from the earlier screens just before
    /// this one is entered. The real runner reads all three; the simulated one
    /// ignores them.
    /// </summary>
    public string GamePath { get; set; } = string.Empty;

    /// <summary>The catalogue preset id the run installs.</summary>
    public string PresetId { get; set; } = "full-duty";

    /// <summary>The preset's display name, for the request and the log.</summary>
    public string PresetName { get; set; } = "Full Duty";

    /// <summary>The detected game build, recorded with the install.</summary>
    public string? GameBuild { get; set; }

    /// <summary>
    /// The exact mods to install, from the customise list. When set, only these
    /// are unpacked. Null falls back to the whole preset.
    /// </summary>
    public IReadOnlyList<string>? ModIds { get; set; }

    /// <summary>
    /// The run log, newest last.
    /// </summary>
    /// <remarks>
    /// Capped at <see cref="MaxLogLines"/>. A real run emits thousands of
    /// lines and an unbounded collection bound to a scroll viewer is a memory
    /// leak with a UI attached. The completion report, not this, is the thing
    /// meant to be kept.
    /// </remarks>
    public ObservableCollection<string> Log { get; } = [];

    private const int MaxLogLines = 400;

    /// <summary>Raised when the run finishes and the wizard should move on.</summary>
    public event EventHandler<InstallReport>? Finished;

    /// <inheritdoc />
    public override string Title => "Install";

    /// <inheritdoc />
    public override bool ShowNavigation => false;

    /// <summary>Counter, as "18 of 41".</summary>
    public string Counter => $"{Completed} of {Total}";

    /// <summary>The edition and build being installed onto, e.g. "GTA V Legacy · build 1.0.3725".</summary>
    public string VersionReadout =>
        $"GTA V {VersionReader.EditionName(Edition)} · build {(string.IsNullOrWhiteSpace(GameBuild) ? "unknown" : GameBuild)}";

    /// <summary>What the last run produced. Null until it finishes.</summary>
    public InstallReport? Report { get; private set; }

    /// <inheritdoc />
    public override void OnEntered()
    {
        // Re-entering must not start a second run. Back is disabled here, but
        // the development step shortcut can still land on this screen twice.
        if (_hasRun)
        {
            return;
        }

        // Writing into files GTA or RagePluginHook has open fails partway and
        // leaves a mess, so the run is held until the game is closed.
        if (_guard is not null && _guard.IsGameRunning(out var process))
        {
            BlockedByGame = true;
            BlockingProcess = process ?? "the game";
            return;
        }

        StartRun();
    }

    /// <summary>Re-checks that the game is closed, then starts the install.</summary>
    public void RetryAfterClosingGame()
    {
        if (_guard is not null && _guard.IsGameRunning(out var process))
        {
            BlockingProcess = process ?? "the game";
            return;
        }

        BlockedByGame = false;
        StartRun();
    }

    private void StartRun()
    {
        _hasRun = true;
        _cancellation = new CancellationTokenSource();

        _clock.Restart();
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += OnTick;
        _ticker.Start();
        OnTick(this, EventArgs.Empty);

        _ = RunAsync(_cancellation.Token);
    }

    /// <summary>Updates the elapsed and estimated-remaining readouts each second.</summary>
    private void OnTick(object? sender, EventArgs e)
    {
        ElapsedText = Format(_clock.Elapsed);

        // A linear extrapolation from progress so far. Rough by nature, so it is
        // shown as an estimate and only once there is enough progress to mean
        // anything — a remaining time from 1% done is noise.
        var fraction = Fraction;
        if (fraction is > 0.03 and < 1.0)
        {
            var remaining = TimeSpan.FromSeconds(_clock.Elapsed.TotalSeconds * (1 - fraction) / fraction);
            RemainingText = $"~{Format(remaining)} left";
        }
        else if (fraction <= 0.03)
        {
            RemainingText = "estimating…";
        }
    }

    private void StopTicker()
    {
        _clock.Stop();
        if (_ticker is not null)
        {
            _ticker.Stop();
            _ticker.Tick -= OnTick;
            _ticker = null;
        }
    }

    private static string Format(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes}:{span.Seconds:00}";

    /// <summary>Stops the run after the current operation.</summary>
    public void Cancel()
    {
        _cancellation?.Cancel();
        StopTicker();
    }

    private async Task RunAsync(CancellationToken token)
    {
        // Progress arrives off the UI thread, so every update is marshalled
        // rather than assigned directly to a bound property.
        var progress = new Progress<InstallProgress>(update =>
            Dispatcher.UIThread.Post(() =>
            {
                PhaseIndex = update.PhaseIndex;
                Phase = update.PhaseName;
                Detail = update.Detail;
                Completed = update.Completed;
                Total = update.Total;
                Fraction = update.Fraction;
                Percent = update.Percent;

                if (update.Log is { Length: > 0 } line)
                {
                    Log.Add(line);

                    while (Log.Count > MaxLogLines)
                    {
                        Log.RemoveAt(0);
                    }
                }
            }));

        try
        {
            var request = new InstallRequest(
                GamePath: GamePath,
                PresetName: PresetName,
                ModCount: Total,
                PresetId: PresetId,
                GameBuild: GameBuild,
                ModIds: ModIds);

            Report = await _runner.RunAsync(request, progress, token).ConfigureAwait(true);

            Dispatcher.UIThread.Post(() =>
            {
                StopTicker();
                PhaseIndex = 7;
                Phase = "Done";
                Fraction = 1;
                Percent = 100;
                ElapsedText = Format(Report.Elapsed);
                RemainingText = "done";
                Detail = Report.IsClean
                    ? "Everything installed cleanly"
                    : $"{Report.NeedsAttention.Count} item needs attention";

                Log.Add($"{Report.Elapsed:mm\\:ss}  done     " +
                        $"{Report.Installed.Count} installed, {Report.NeedsAttention.Count} need attention");

                Finished?.Invoke(this, Report);
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled deliberately. The wizard stays put.
        }
    }

    partial void OnCompletedChanged(int value) => OnPropertyChanged(nameof(Counter));

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(Counter));
}
