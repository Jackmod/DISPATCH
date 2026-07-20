using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Audio;
using Dispatch.Core.Detection;
using Dispatch.Core.Imagery;
using Dispatch.Core.Installation;
using Dispatch.Core.Profiles;
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
    private readonly IProfileStore? _profiles;
    private readonly Core.Configuration.ConfigInstaller? _config;
    private readonly IInstallRecordStore? _installRecords;

    /// <summary>Constructs the flow with its six screens.</summary>
    public WizardViewModel(
        IInstallRunner? runner = null,
        IUserBackgrounds? backgrounds = null,
        ICallsignVoice? voice = null,
        IProfileStore? profiles = null,
        IGameLocator? locator = null,
        IVersionReader? versions = null,
        Core.Maintenance.IQuarantine? quarantine = null,
        Core.Configuration.ConfigInstaller? config = null,
        IInstallRecordStore? installRecords = null,
        Core.Detection.IGameProcessGuard? gameGuard = null,
        Core.Platform.IDefenderService? defender = null)
    {
        _profiles = profiles;
        _config = config;
        _installRecords = installRecords;

        var install = new InstallStep(runner ?? new SimulatedInstallRunner(), gameGuard);

        Steps =
        [
            new WelcomeStep(),
            new WhatThisIsStep(),
            new LocateGameStep(locator, versions, quarantine, defender),
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
            // The window handles Completed by building the officer and opening
            // the launcher, which persists on the way; nothing to do here but
            // raise it.
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
    /// Writes what the wizard collected to the profile store.
    /// </summary>
    /// <remarks>
    /// Assembled here rather than in the screens: each screen owns its own
    /// presentation state, and the wizard is the only thing that can see all
    /// of it at once. Without a store injected this is a no-op, which is what
    /// keeps the flow testable without touching a disk.
    /// </remarks>
    public async Task PersistAsync(CancellationToken cancellationToken = default) =>
        await BuildOfficerAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Assembles the officer from what the wizard collected, persists it, and
    /// returns it so the launcher can open on the right identity.
    /// </summary>
    /// <remarks>
    /// Returns null when there is nothing to build — no officer name entered —
    /// so a caller opening the launcher gets the placeholder identity rather
    /// than a half-filled one.
    /// </remarks>
    public async Task<OfficerProfile?> BuildOfficerAsync(CancellationToken cancellationToken = default)
    {
        var officerStep = Steps.OfType<OfficerStep>().FirstOrDefault();
        var locateStep = Steps.OfType<LocateGameStep>().FirstOrDefault();

        if (officerStep is null || string.IsNullOrWhiteSpace(officerStep.OfficerName))
        {
            return null;
        }

        var officer = OfficerProfile.Create(officerStep.OfficerName.Trim()) with
        {
            Agency = ParseAgency(officerStep.Agency),
            CallsignDivision = officerStep.CallsignDivision,
            CallsignPhonetic = officerStep.CallsignPhonetic,
            CallsignBeat = officerStep.CallsignBeat,
            DepartmentName = officerStep.DepartmentName,
            AirUnitCallsign = officerStep.AirUnitCallsign,
            ControlProfile = officerStep.ControlProfile,
        };

        // No store injected during a test still yields the officer, so the
        // handoff is exercisable without a disk.
        var gamePath = locateStep?.Selected?.Path;
        if (_profiles is not null)
        {
            var existing = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
            var updated = existing.WithOfficer(officer) with
            {
                GamePath = gamePath ?? existing.GamePath,
            };

            await _profiles.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            gamePath ??= updated.GamePath;
        }

        // The install ran before the officer existed, so its config files hold
        // placeholder callsign and name. Now that the officer is known, write the
        // real values in — re-applying is idempotent, so the keybinds already set
        // do not move; only the officer's own details change.
        await ApplyOfficerConfigAsync(officer, gamePath, cancellationToken).ConfigureAwait(false);

        return officer;
    }

    /// <summary>
    /// Writes the officer's callsign, name, department and air unit into every
    /// installed mod's config. Reusable so a later change to the officer can call
    /// the same path and have it land in the files.
    /// </summary>
    public async Task ApplyOfficerConfigAsync(
        OfficerProfile officer, string? gamePath, CancellationToken cancellationToken = default)
    {
        if (_config is null || _installRecords is null || string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        var record = await _installRecords.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (record is null || record.ModIds.Count == 0)
        {
            return;
        }

        var fallback = Core.Configuration.OfficerValues.Default;
        var values = new Core.Configuration.OfficerValues(
            Callsign: Blank(officer.Callsign) ? fallback.Callsign : officer.Callsign,
            OfficerName: Blank(officer.Name) ? fallback.OfficerName : officer.Name,
            Department: Blank(officer.DepartmentName) ? fallback.Department : officer.DepartmentName,
            AirUnitCallsign: Blank(officer.AirUnitCallsign) ? fallback.AirUnitCallsign : officer.AirUnitCallsign);

        await _config.ApplyAsync(gamePath, record.ModIds, values, cancellationToken).ConfigureAwait(false);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static Agency ParseAgency(string agency) =>
        Enum.TryParse<Agency>(agency, ignoreCase: true, out var parsed) ? parsed : Agency.Lspd;

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

        // The install screen starts its run in OnEntered, so it must be told what
        // to install first — the game folder from screen 3 and the preset from
        // screen 4, neither of which it can see for itself.
        if (CurrentStep is InstallStep install)
        {
            PopulateInstall(install);
        }

        // The preset screen shows real free space on the game drive.
        if (CurrentStep is ChoosePresetStep preset)
        {
            var locate = Steps.OfType<LocateGameStep>().FirstOrDefault();
            preset.SetGameDrive(locate?.Selected?.Path);
        }

        Subscribe(CurrentStep);
        CurrentStep.OnEntered();
    }

    /// <summary>Feeds the install screen the game path, preset and expected mod count.</summary>
    private void PopulateInstall(InstallStep install)
    {
        var locate = Steps.OfType<LocateGameStep>().FirstOrDefault();
        var preset = Steps.OfType<ChoosePresetStep>().FirstOrDefault();

        if (locate?.Selected is { } candidate)
        {
            install.GamePath = candidate.Path;
            install.GameBuild = candidate.Build;
            install.Edition = candidate.Edition;
        }

        var tier = preset?.Selected?.Tier ?? PresetTier.FullDuty;
        install.PresetId = PresetIdFor(tier);
        install.PresetName = preset?.Selected?.Name ?? "Full Duty";

        // The customise list is the source of truth for what installs. When the
        // user has ticked mods, those exact ids drive the run; otherwise the
        // whole preset does.
        var selected = preset?.SelectedModIds;
        if (selected is { Count: > 0 })
        {
            install.ModIds = selected;
            install.Total = selected.Count;
        }
        else
        {
            install.ModIds = null;
            var mods = Core.Catalogue.ModCatalogue.ModsFor(install.PresetId);
            if (mods.Count > 0)
            {
                install.Total = mods.Count;
            }
        }
    }

    private static string PresetIdFor(PresetTier tier) => tier switch
    {
        PresetTier.Standard => "standard",
        PresetTier.FullDuty => "full-duty",
        _ => "realism",
    };

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
