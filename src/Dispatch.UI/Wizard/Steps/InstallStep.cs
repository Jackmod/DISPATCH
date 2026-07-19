using CommunityToolkit.Mvvm.ComponentModel;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>
/// Screen 5. The screen goes quiet: phase, detail line, counter, and the
/// lightbar rail as the progress indicator.
/// </summary>
/// <remarks>
/// Driven by mock progress until InstallRunner lands. The phase names and the
/// shape of the readout are final, so wiring the real runner is a data change
/// rather than a redesign.
/// </remarks>
public sealed partial class InstallStep : WizardStep
{
    /// <summary>The seven phases, in order.</summary>
    public static readonly string[] Phases =
    [
        "Collecting files",
        "Checking compatibility",
        "Backing up",
        "Placing files",
        "Writing configuration",
        "Installing textures",
        "Verifying",
    ];

    [ObservableProperty]
    private int _phaseIndex = 3;

    [ObservableProperty]
    private string _detail = "Stop The Ped 8.5 — extracting";

    [ObservableProperty]
    private int _completed = 18;

    [ObservableProperty]
    private int _total = 41;

    [ObservableProperty]
    private bool _isLogExpanded;

    /// <inheritdoc />
    public override string Title => "Install";

    /// <inheritdoc />
    public override bool ShowNavigation => false;

    /// <summary>Current phase name, shown large.</summary>
    public string Phase => Phases[Math.Clamp(PhaseIndex, 0, Phases.Length - 1)];

    /// <summary>Counter, as "18 of 41".</summary>
    public string Counter => $"{Completed} of {Total}";

    partial void OnPhaseIndexChanged(int value) => OnPropertyChanged(nameof(Phase));

    partial void OnCompletedChanged(int value) => OnPropertyChanged(nameof(Counter));

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(Counter));
}
