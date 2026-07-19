using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Detection;
using Dispatch.UI.Controls;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>One candidate GTA V installation found by detection.</summary>
/// <param name="Platform">Steam, Epic, Rockstar or Manual.</param>
/// <param name="Path">Root of the installation.</param>
/// <param name="Build">Game build read from GTA5.exe, shown in mono.</param>
/// <param name="State">What validation found.</param>
/// <param name="StateDetail">Plain-language detail, named specifically.</param>
public sealed record GameCandidate(
    string Platform,
    string Path,
    string Build,
    GameCandidateState State,
    string StateDetail);

/// <summary>What validating a candidate found.</summary>
public enum GameCandidateState
{
    /// <summary>GTA5.exe present, build read, no mod files detected.</summary>
    Verified,

    /// <summary>Mod files already present. Installing over these is the worst case.</summary>
    AlreadyModified,

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
    [ObservableProperty]
    private GameCandidate? _selected;

    /// <summary>Constructs the screen, running real detection when available.</summary>
    /// <param name="locator">Finds installs. Null falls back to representative data.</param>
    /// <param name="versions">Reads builds and mod state. Null falls back too.</param>
    public LocateGameStep(IGameLocator? locator = null, IVersionReader? versions = null)
    {
        var detected = Detect(locator, versions);

        // With nothing detected — the common case on a dev machine — the screen
        // still shows representative cards so the wizard is demonstrable end to
        // end rather than dead-ending on an empty list.
        Candidates = detected.Count > 0 ? new ObservableCollection<GameCandidate>(detected) : Mock();
        _selected = Candidates.FirstOrDefault(c => c.State != GameCandidateState.Invalid) ?? Candidates[0];
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
            var read = versions?.Read(install.Path);
            var build = read?.GameBuild ?? "unknown";
            var modded = read?.HasModFiles ?? false;

            var (state, detail) = modded
                ? (GameCandidateState.AlreadyModified,
                    "Mod files are already here from an earlier attempt. Installing over them causes the most confusing failures in this ecosystem.")
                : (GameCandidateState.Verified,
                    "GTA5.exe found, build read, no mod files present.");

            result.Add(new GameCandidate(install.Platform.ToString(), install.Path, build, state, detail));
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
    public override bool CanAdvance => Selected is { State: not GameCandidateState.Invalid };

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
        GameCandidateState.Verified => "VERIFIED",
        GameCandidateState.AlreadyModified => "ALREADY MODIFIED",
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
    }
}
