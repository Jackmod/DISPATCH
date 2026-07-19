using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Audio;
using Dispatch.Core.Imagery;
using Dispatch.Core.Installation;
using Dispatch.UI.Wizard.Steps;

namespace Dispatch.UI.Wizard;

/// <summary>
/// Owns the first-run flow: which screen is current, whether it may advance,
/// and the data collected along the way.
/// </summary>
/// <remarks>
/// The wizard owns the model and the screens bind to it, which is what makes
/// Back non-destructive — moving backwards changes an index, not any data.
/// </remarks>
public sealed partial class WizardViewModel : ObservableObject
{
    [ObservableProperty]
    private WizardStep _currentStep;

    [ObservableProperty]
    private int _currentIndex;

    /// <summary>Constructs the flow with its six screens.</summary>
    /// <param name="backgrounds">
    /// Resolves optional user-supplied background images. Null falls back to
    /// the original vector scenes everywhere.
    /// </param>
    public WizardViewModel(
        IInstallRunner? runner = null,
        IUserBackgrounds? backgrounds = null,
        ICallsignVoice? voice = null)
    {
        var install = new InstallStep(runner ?? new SimulatedInstallRunner());

        Steps =
        [
            new WelcomeStep(),
            new WhatThisIsStep(),
            new LocateGameStep(),
            new ChoosePresetStep(backgrounds),
            install,
            new OfficerStep(voice),
        ];

        // Subscribed after Steps exists, since the handler reads it. The
        // install screen hides its own navigation, so it advances the wizard
        // itself rather than leaving the user on a finished run with no way
        // forward.
        var installIndex = Steps.IndexOf(install);
        install.Finished += (_, _) => Dispatcher.UIThread.Post(() => GoTo(installIndex + 1));

        _currentStep = Steps[0];
        _currentStep.OnEntered();
        Subscribe(_currentStep);
    }

    /// <summary>The six screens, in order.</summary>
    public ObservableCollection<WizardStep> Steps { get; }

    /// <summary>Total screens, for the progress rail.</summary>
    public int StepCount => Steps.Count;

    /// <summary>Position in the flow, as "STEP 3 OF 6".</summary>
    public string StepCounter => $"STEP {CurrentIndex + 1} OF {StepCount}";

    /// <summary>True when there is a screen before this one.</summary>
    public bool CanGoBack => CurrentIndex > 0;

    /// <summary>True when this is the last screen, whose action finishes the flow.</summary>
    public bool IsLastStep => CurrentIndex == Steps.Count - 1;

    /// <summary>
    /// True when the current screen is satisfied.
    /// </summary>
    /// <remarks>
    /// Deliberately not conditioned on a next screen existing. The final screen
    /// is not a dead end — its action is Finish — and requiring a successor
    /// left the button permanently disabled with the flow complete and no way
    /// out of it.
    /// </remarks>
    public bool CanGoNext => CurrentStep.CanAdvance;

    /// <summary>Raised when the last screen's action is taken.</summary>
    public event EventHandler? Completed;

    /// <summary>Advances, or finishes if this is the last screen.</summary>
    [RelayCommand]
    private void Next()
    {
        if (!CanGoNext)
        {
            return;
        }

        if (IsLastStep)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        GoTo(CurrentIndex + 1);
    }

    /// <summary>Moves to the previous screen, preserving everything entered.</summary>
    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
        {
            GoTo(CurrentIndex - 1);
        }
    }

    /// <summary>
    /// Jumps to a screen by index. Used by the rail and by the install screen,
    /// which advances itself when the run finishes.
    /// </summary>
    public void GoTo(int index)
    {
        if (index < 0 || index >= Steps.Count || index == CurrentIndex)
        {
            return;
        }

        Unsubscribe(CurrentStep);

        CurrentIndex = index;
        CurrentStep = Steps[index];

        Subscribe(CurrentStep);
        CurrentStep.OnEntered();
    }

    partial void OnCurrentStepChanged(WizardStep value) => RaiseNavigation();

    partial void OnCurrentIndexChanged(int value) => RaiseNavigation();

    private void Subscribe(WizardStep step) => step.PropertyChanged += OnStepPropertyChanged;

    private void Unsubscribe(WizardStep step) => step.PropertyChanged -= OnStepPropertyChanged;

    // A screen becoming satisfied has to re-enable the footer, so CanAdvance
    // changes are forwarded rather than only read once on entry.
    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WizardStep.CanAdvance)
            or nameof(WizardStep.AdvanceLabel)
            or nameof(WizardStep.ShowNavigation))
        {
            RaiseNavigation();
        }
    }

    private void RaiseNavigation()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepCounter));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }
}
