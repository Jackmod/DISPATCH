using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Detection;
using Dispatch.Core.Maintenance;
using Dispatch.UI.Controls;
using Dispatch.UI.Launcher;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>One candidate GTA V installation found by detection.</summary>
/// <param name="Platform">Steam, Epic, Rockstar or Manual.</param>
/// <param name="Path">Root of the installation.</param>
/// <param name="Build">Game build read from GTA5.exe, shown in mono.</param>
/// <param name="State">What validation found.</param>
/// <param name="StateDetail">Plain-language detail, named specifically.</param>
/// <param name="Edition">Legacy, Enhanced or Unknown.</param>
public sealed record GameCandidate(
    string Platform,
    string Path,
    string Build,
    GameCandidateState State,
    string StateDetail,
    GameEdition Edition = GameEdition.Legacy)
{
    /// <summary>A short edition label for the card, e.g. "Legacy".</summary>
    public string EditionLabel => VersionReader.EditionName(Edition);

    /// <summary>Whether this is the supported Legacy edition.</summary>
    public bool IsLegacyEdition => Edition == GameEdition.Legacy;

    /// <summary>Whether this is the unsupported Enhanced edition.</summary>
    public bool IsEnhancedEdition => Edition == GameEdition.Enhanced;
}

/// <summary>One numbered instruction in the verify-your-files guide.</summary>
/// <param name="Number">Its position, shown in the badge.</param>
/// <param name="Text">What to do.</param>
public sealed record VerifyStep(int Number, string Text);

/// <summary>What validating a candidate found.</summary>
public enum GameCandidateState
{
    /// <summary>GTA5.exe present, build read, no mod files detected.</summary>
    Verified,

    /// <summary>Mod files already present. Installing over these is the worst case.</summary>
    AlreadyModified,

    /// <summary>A GTA V folder, but the Enhanced edition — the mod stack cannot run on it.</summary>
    WrongEdition,

    /// <summary>Not a GTA V install at all.</summary>
    Invalid,
}

/// <summary>
/// Screen 3. Finds GTA V and validates the choice.
/// </summary>
/// <remarks>
/// Candidates are mock data until GameLocator lands. The shape is real: the
/// screen already handles more than one install being found, which matters
/// because someone with both a Steam and an Epic copy silently getting the
/// wrong one is a miserable failure to diagnose later.
/// </remarks>
public sealed partial class LocateGameStep : WizardStep
{
    private readonly IVersionReader? _versions;
    private readonly Core.Platform.IDefenderService? _defender;

    [ObservableProperty]
    private GameCandidate? _selected;

    /// <summary>Whether to offer the Defender exclusion (available on this machine).</summary>
    public bool ShowDefenderOffer => _defender?.IsAvailable == true;

    /// <summary>Status line for the Defender exclusion.</summary>
    [ObservableProperty]
    private string _defenderStatus = "Recommended — stops Defender quarantining Script Hook V.";

    /// <summary>Whether the Defender action is mid-flight.</summary>
    [ObservableProperty]
    private bool _defenderBusy;

    /// <summary>Whether the folder is already excluded, so the button can hide.</summary>
    [ObservableProperty]
    private bool _defenderDone;

    /// <summary>Adds a Windows Defender exclusion for the selected game folder.</summary>
    public async Task AddDefenderExclusionAsync()
    {
        if (_defender is null || Selected is null || DefenderBusy)
        {
            return;
        }

        DefenderBusy = true;
        DefenderStatus = "Asking Windows to add the exclusion…";

        var ok = await _defender.AddExclusionAsync(Selected.Path).ConfigureAwait(true);

        DefenderBusy = false;
        DefenderDone = ok;
        DefenderStatus = ok
            ? "Done — the game folder is excluded from Defender."
            : "Not added. You can add it later, or approve the Windows prompt when it appears.";
    }

    /// <summary>Whether the folder cleaner overlay is open on this screen.</summary>
    [ObservableProperty]
    private bool _isCleanerOpen;

    /// <summary>Whether the user has confirmed they verified their game files.</summary>
    [ObservableProperty]
    private bool _integrityVerified;

    /// <summary>Constructs the screen, running real detection when available.</summary>
    /// <param name="locator">Finds installs. Null falls back to representative data.</param>
    /// <param name="versions">Reads builds and mod state. Null falls back too.</param>
    /// <param name="quarantine">Backs the cleaner. Null falls back to a temp store.</param>
    public LocateGameStep(
        IGameLocator? locator = null,
        IVersionReader? versions = null,
        IQuarantine? quarantine = null,
        Core.Platform.IDefenderService? defender = null)
    {
        _versions = versions;
        _defender = defender;

        var detected = Detect(locator, versions);

        // With nothing detected — the common case on a dev machine — the screen
        // still shows representative cards so the wizard is demonstrable end to
        // end rather than dead-ending on an empty list.
        Candidates = detected.Count > 0 ? new ObservableCollection<GameCandidate>(detected) : Mock();
        _selected = Candidates.FirstOrDefault(c =>
            c.State is GameCandidateState.Verified or GameCandidateState.AlreadyModified) ?? Candidates[0];

        Cleaner = new CleanerViewModel(quarantine: quarantine);
    }

    /// <summary>The folder cleaner, shown as an overlay on this screen.</summary>
    public CleanerViewModel Cleaner { get; }

    /// <summary>Opens the cleaner against the selected folder and starts a scan.</summary>
    public async Task OpenCleanerAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(Selected.Path))
        {
            return;
        }

        IsCleanerOpen = true;
        await Cleaner.ScanAsync(Selected.Path).ConfigureAwait(true);
    }

    /// <summary>Closes the cleaner overlay and revalidates the folder, which the clean may have changed.</summary>
    public void CloseCleaner()
    {
        IsCleanerOpen = false;

        // A clean can flip a folder from "already modified" to "verified", so the
        // card is re-read rather than left showing the pre-clean state.
        if (Selected is not null)
        {
            Revalidate(Selected);
        }
    }

    /// <summary>
    /// Adds a hand-picked folder as a candidate and selects it, validating it the
    /// same way a detected install is validated.
    /// </summary>
    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var candidate = Validate("Manual", path);

        // Replace an earlier manual pick rather than stacking duplicates.
        var existing = Candidates.FirstOrDefault(c =>
            string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Candidates.Remove(existing);
        }

        Candidates.Insert(0, candidate);
        Selected = candidate;
    }

    private void Revalidate(GameCandidate candidate)
    {
        var index = Candidates.IndexOf(candidate);
        if (index < 0)
        {
            return;
        }

        var refreshed = Validate(candidate.Platform, candidate.Path);
        Candidates[index] = refreshed;
        Selected = refreshed;
    }

    /// <summary>The edition of a folder, by which executable is present.</summary>
    private static GameEdition EditionOf(string path) =>
        System.IO.File.Exists(System.IO.Path.Combine(path, "GTA5.exe")) ? GameEdition.Legacy
        : System.IO.File.Exists(System.IO.Path.Combine(path, "GTA5_Enhanced.exe")) ? GameEdition.Enhanced
        : GameEdition.Unknown;

    /// <summary>Reads a folder and classifies it, exactly as detection does.</summary>
    private GameCandidate Validate(string platform, string path)
    {
        var edition = EditionOf(path);

        if (edition == GameEdition.Enhanced)
        {
            return new GameCandidate(
                platform, path, "Enhanced", GameCandidateState.WrongEdition,
                "This is GTA V Enhanced. LSPDFR, RagePluginHook and Script Hook V only run on the "
                + "Legacy edition, so nothing here would ever load. Install the Legacy edition to continue.",
                GameEdition.Enhanced);
        }

        if (edition != GameEdition.Legacy)
        {
            return new GameCandidate(
                platform, path, "unknown", GameCandidateState.Invalid,
                "No GTA5.exe here — this is not a Grand Theft Auto V (Legacy) folder.",
                GameEdition.Unknown);
        }

        var read = _versions?.Read(path);
        var build = read?.GameBuild ?? "unknown";
        var modded = read?.HasModFiles ?? false;

        var (state, detail) = modded
            ? (GameCandidateState.AlreadyModified,
                "Mod files are already here from an earlier attempt. Installing over them causes the most confusing failures in this ecosystem.")
            : (GameCandidateState.Verified,
                "GTA5.exe found, Legacy build read, no mod files present.");

        return new GameCandidate(platform, path, build, state, detail, GameEdition.Legacy);
    }

    private static IReadOnlyList<GameCandidate> Detect(IGameLocator? locator, IVersionReader? versions)
    {
        if (locator is null)
        {
            return [];
        }

        var result = new List<GameCandidate>();

        foreach (var install in locator.Locate())
        {
            var edition = EditionOf(install.Path);

            if (edition == GameEdition.Enhanced)
            {
                result.Add(new GameCandidate(
                    install.Platform.ToString(), install.Path, "Enhanced", GameCandidateState.WrongEdition,
                    "This is GTA V Enhanced. The police-mod stack only runs on the Legacy edition.",
                    GameEdition.Enhanced));
                continue;
            }

            var read = versions?.Read(install.Path);
            var build = read?.GameBuild ?? "unknown";
            var modded = read?.HasModFiles ?? false;

            var (state, detail) = modded
                ? (GameCandidateState.AlreadyModified,
                    "Mod files are already here from an earlier attempt. Installing over them causes the most confusing failures in this ecosystem.")
                : (GameCandidateState.Verified,
                    "GTA5.exe found, Legacy build read, no mod files present.");

            result.Add(new GameCandidate(install.Platform.ToString(), install.Path, build, state, detail, GameEdition.Legacy));
        }

        return result;
    }

    private static ObservableCollection<GameCandidate> Mock() =>
    [
        new GameCandidate(
            "Steam",
            @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
            "1.0.3725",
            GameCandidateState.Verified,
            "GTA5.exe found, build read, no mod files present."),
        new GameCandidate(
            "Epic Games",
            @"D:\Epic\GTAV",
            "1.0.3407",
            GameCandidateState.AlreadyModified,
            "Found dinput8.dll, ScriptHookV.dll and a plugins folder from an earlier attempt."),
    ];

    /// <summary>Everything detection turned up. All of them are shown, never one silently.</summary>
    public ObservableCollection<GameCandidate> Candidates { get; }

    /// <inheritdoc />
    public override string Title => "Locate GTA V";

    /// <inheritdoc />
    /// <remarks>
    /// Gated on the integrity acknowledgement as well as a valid folder. Verifying
    /// the game files first repairs anything missing or altered, so mods install
    /// onto a clean, known-good copy — skipping it is behind a large share of the
    /// crashes that are impossible to diagnose afterwards.
    /// </remarks>
    public override bool CanAdvance =>
        Selected is { State: GameCandidateState.Verified or GameCandidateState.AlreadyModified }
        && IntegrityVerified;

    /// <summary>The edition of the current selection, e.g. "Legacy".</summary>
    public string SelectedEdition => Selected?.EditionLabel ?? "Unknown";

    /// <summary>The build of the current selection, in mono.</summary>
    public string SelectedBuild => Selected?.Build ?? "unknown";

    /// <summary>Whether the current selection is the supported Legacy edition.</summary>
    public bool SelectedIsLegacy => Selected?.Edition == GameEdition.Legacy;

    /// <summary>Whether the current selection is the unsupported Enhanced edition.</summary>
    public bool SelectedIsEnhanced => Selected?.State == GameCandidateState.WrongEdition;

    /// <summary>The launcher name to address the verify steps to.</summary>
    public string VerifyPlatform => Selected?.Platform switch
    {
        "Steam" => "Steam",
        "Epic" => "the Epic Games Launcher",
        "Rockstar" => "the Rockstar Games Launcher",
        _ => "your game launcher",
    };

    /// <summary>Platform-specific steps for verifying the game files.</summary>
    public IReadOnlyList<VerifyStep> VerifySteps => (Selected?.Platform switch
    {
        "Steam" => new[]
        {
            "Open Steam and go to your Library.",
            "Right-click Grand Theft Auto V and choose Properties.",
            "Open the Installed Files tab.",
            "Click “Verify integrity of game files” and let it finish.",
        },
        "Epic" => new[]
        {
            "Open the Epic Games Launcher and go to your Library.",
            "Click the three dots (…) on Grand Theft Auto V.",
            "Choose Manage, then click Verify.",
            "Wait for it to finish.",
        },
        "Rockstar" => new[]
        {
            "Open the Rockstar Games Launcher.",
            "Open Settings, then pick Grand Theft Auto V under your installed games.",
            "Click Verify Integrity and let it finish.",
        },
        _ => new[]
        {
            "Open the launcher you installed GTA V through (Steam, Epic or Rockstar).",
            "Find its verify or repair option for Grand Theft Auto V.",
            "Run it and let it finish.",
        },
    }).Select((text, i) => new VerifyStep(i + 1, text)).ToList();

    /// <summary>Status strip tone for the current selection.</summary>
    public StatusTone SelectedTone => Selected?.State switch
    {
        GameCandidateState.Verified => StatusTone.Good,
        GameCandidateState.AlreadyModified => StatusTone.Warning,
        _ => StatusTone.Bad,
    };

    /// <summary>Status strip label for the current selection.</summary>
    public string SelectedLabel => Selected?.State switch
    {
        GameCandidateState.Verified => "VERIFIED · LEGACY",
        GameCandidateState.AlreadyModified => "ALREADY MODIFIED",
        GameCandidateState.WrongEdition => "ENHANCED — NOT SUPPORTED",
        _ => "NOT A GTA V INSTALL",
    };

    /// <summary>Whether to offer the cleaner for this selection.</summary>
    public bool ShowCleanOffer => Selected?.State == GameCandidateState.AlreadyModified;

    partial void OnSelectedChanged(GameCandidate? value)
    {
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(SelectedTone));
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(ShowCleanOffer));
        OnPropertyChanged(nameof(VerifyPlatform));
        OnPropertyChanged(nameof(VerifySteps));
        OnPropertyChanged(nameof(SelectedEdition));
        OnPropertyChanged(nameof(SelectedBuild));
        OnPropertyChanged(nameof(SelectedIsLegacy));
        OnPropertyChanged(nameof(SelectedIsEnhanced));
    }

    partial void OnIntegrityVerifiedChanged(bool value) => OnPropertyChanged(nameof(CanAdvance));
}
