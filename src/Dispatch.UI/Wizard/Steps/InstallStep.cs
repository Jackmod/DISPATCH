using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private CancellationTokenSource? _cancellation;
    private bool _hasRun;

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

    /// <summary>Constructs the screen against a runner.</summary>
    public InstallStep(IInstallRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

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

        _hasRun = true;
        _cancellation = new CancellationTokenSource();
        _ = RunAsync(_cancellation.Token);
    }

    /// <summary>Stops the run after the current operation.</summary>
    public void Cancel() => _cancellation?.Cancel();

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
                GamePath: string.Empty,
                PresetName: "Full Duty",
                ModCount: Total);

            Report = await _runner.RunAsync(request, progress, token).ConfigureAwait(true);

            Dispatcher.UIThread.Post(() =>
            {
                PhaseIndex = 7;
                Phase = "Done";
                Fraction = 1;
                Percent = 100;
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
