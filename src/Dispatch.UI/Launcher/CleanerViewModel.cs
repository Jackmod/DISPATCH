using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>A single mod file the cleaner will move, for the warning list.</summary>
public sealed class CleanRow
{
    /// <summary>Constructs a row from a scanned candidate.</summary>
    public CleanRow(CleanCandidate candidate)
    {
        Candidate = candidate;
        Path = candidate.RelativePath;
        Size = FormatSize(candidate.SizeBytes);
        Reason = candidate.Reason;
    }

    /// <summary>The candidate this row represents.</summary>
    public CleanCandidate Candidate { get; }

    /// <summary>Its path.</summary>
    public string Path { get; }

    /// <summary>Its size, formatted.</summary>
    public string Size { get; }

    /// <summary>Why the scanner classified it this way.</summary>
    public string Reason { get; }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024:0.0} MB",
    };
}

/// <summary>The steps of the clean modal, in order.</summary>
public enum CleanerStage
{
    /// <summary>Reading the folder against the install record.</summary>
    Scanning,

    /// <summary>Showing what was found, waiting to proceed.</summary>
    Warning,

    /// <summary>Moving the files to quarantine.</summary>
    Cleaning,

    /// <summary>Cleaned; telling the user to verify their game files.</summary>
    Verify,
}

/// <summary>
/// Drives the Clean GTA folder modal as a short, guided sequence: warn, clean,
/// verify, close.
/// </summary>
/// <remarks>
/// Detection leans on the install record rather than guesswork — Dispatch placed
/// the mods, so it knows exactly which files are its own and marks them for
/// removal with certainty; the heuristics only fill in for a folder modded by
/// hand. Nothing is deleted: files move to quarantine and can be restored. The
/// last step is the important one — after the mods are gone, verifying the game
/// files restores anything a mod had overwritten, so the base is genuinely clean.
/// </remarks>
public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly IQuarantine _quarantine;
    private readonly IInstallRecordStore _records;
    private CleanPlan? _plan;

    // Warning is the resting stage: opened without a folder to scan, the modal
    // shows an empty "nothing to clean" state rather than a spinner that never
    // resolves. A real scan flips it to Scanning immediately.
    [ObservableProperty]
    private CleanerStage _stage = CleanerStage.Warning;

    [ObservableProperty]
    private int _filesScanned;

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private bool _cleaningInProgress;

    /// <summary>How far the clean has got, 0 to 1, for the progress bar.</summary>
    [ObservableProperty]
    private double _cleanProgress;

    /// <summary>What the clean is doing right now — the file being moved, or the stage.</summary>
    [ObservableProperty]
    private string _cleanCurrentItem = string.Empty;

    /// <summary>Files moved so far this clean.</summary>
    [ObservableProperty]
    private int _cleanDone;

    /// <summary>Total files this clean will move.</summary>
    [ObservableProperty]
    private int _cleanTotal;

    [ObservableProperty]
    private string _cleanSummary = string.Empty;

    /// <summary>Whether the user has ticked the "I verified" box.</summary>
    [ObservableProperty]
    private bool _verifyAcknowledged;

    /// <summary>Constructs the cleaner view model.</summary>
    /// <remarks>
    /// When no store is injected it falls back to the real ones under
    /// LOCALAPPDATA, the same convention the other launcher screens follow — so a
    /// bare <c>new CleanerViewModel()</c> in the launcher already reads the install
    /// record and quarantines to the right place.
    /// </remarks>
    public CleanerViewModel(IQuarantine? quarantine = null, IInstallRecordStore? records = null)
    {
        var paths = new AppPaths();
        _quarantine = quarantine ?? new Quarantine(
            paths.QuarantineDirectory, NullLogger<Quarantine>.Instance);
        _records = records ?? new InstallRecordStore(paths, NullLogger<InstallRecordStore>.Instance);
    }

    /// <summary>The mod files that will be moved to quarantine.</summary>
    public ObservableCollection<CleanRow> ToRemove { get; } = [];

    /// <summary>How many files will be removed.</summary>
    public int RemoveCount => ToRemove.Count;

    /// <summary>Total size of what will be removed.</summary>
    public string RemoveSize => FormatSize(ToRemove.Sum(r => r.Candidate.SizeBytes));

    /// <summary>How many protected files were found and will be left alone.</summary>
    public int ProtectedCount => _plan?.Protected.Count ?? 0;

    /// <summary>Whether any protected files were found, for the "left untouched" note.</summary>
    public bool HasProtected => ProtectedCount > 0;

    /// <summary>Whether there is anything to clean.</summary>
    public bool HasWork => RemoveCount > 0;

    /// <summary>Whether the warning step should offer the Clean button.</summary>
    public bool ShowCleanButton => Stage == CleanerStage.Warning && HasWork;

    /// <summary>Whether the warning step is empty and offers only Close.</summary>
    public bool ShowCloseWhenEmpty => Stage == CleanerStage.Warning && !HasWork;

    /// <summary>Whether to nudge the user to tick the verify box.</summary>
    public bool ShowVerifyHint => Stage == CleanerStage.Verify && !VerifyAcknowledged;

    /// <summary>The launcher name for the verify steps, from the game path.</summary>
    public string VerifyPlatform =>
        GamePath.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ? "Steam"
        : GamePath.Contains("Epic", StringComparison.OrdinalIgnoreCase) ? "the Epic Games Launcher"
        : "your game launcher";

    /// <summary>Platform-specific verify steps for the final screen, numbered.</summary>
    public IReadOnlyList<string> VerifySteps =>
        Numbered(GamePath.Contains("steamapps", StringComparison.OrdinalIgnoreCase)
            ? ["Open Steam and go to your Library.",
               "Right-click Grand Theft Auto V and choose Properties.",
               "Open the Installed Files tab.",
               "Click “Verify integrity of game files” and let it finish."]
        : GamePath.Contains("Epic", StringComparison.OrdinalIgnoreCase)
            ? ["Open the Epic Games Launcher and go to your Library.",
               "Click the three dots (…) on Grand Theft Auto V.",
               "Choose Manage, then click Verify.",
               "Wait for it to finish."]
            : ["Open the launcher you installed GTA V through.",
               "Find its verify or repair option for Grand Theft Auto V.",
               "Run it and let it finish."]);

    private static IReadOnlyList<string> Numbered(IReadOnlyList<string> steps) =>
        steps.Select((step, i) => $"{i + 1}.  {step}").ToList();

    /// <summary>A short caption for the modal header, naming the current step.</summary>
    public string StepCaption => Stage switch
    {
        CleanerStage.Scanning => "Checking your folder…",
        CleanerStage.Warning => "Step 1 of 3  ·  Review",
        CleanerStage.Cleaning => "Step 2 of 3  ·  Cleaning",
        CleanerStage.Verify => "Step 3 of 3  ·  Verify",
        _ => string.Empty,
    };

    /// <summary>Whether the Done button may close the modal.</summary>
    public bool CanClose => Stage == CleanerStage.Verify && VerifyAcknowledged;

    /// <summary>Progress as "12 of 40 files", shown beside the bar while cleaning.</summary>
    public string CleanCounter => CleanTotal > 0 ? $"{CleanDone} of {CleanTotal} files" : string.Empty;

    partial void OnCleanTotalChanged(int value) => OnPropertyChanged(nameof(CleanCounter));

    /// <summary>Raised when the modal should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Scans the folder against the install record and moves to the warning.</summary>
    public async Task ScanAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        GamePath = gamePath;
        Stage = CleanerStage.Scanning;
        FilesScanned = 0;
        VerifyAcknowledged = false;
        ToRemove.Clear();

        // The install record is the source of truth: every file Dispatch placed
        // is a known mod file, so the scan is certain rather than heuristic.
        var known = await LoadKnownFilesAsync(cancellationToken).ConfigureAwait(true);
        var cleaner = new FolderCleaner(NullLogger<FolderCleaner>.Instance, known);

        var progress = new Progress<int>(count => Dispatcher.UIThread.Post(() => FilesScanned = count));
        _plan = await Task.Run(() => cleaner.Scan(gamePath, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        // Everything the scanner is confident about — the mod files — is queued.
        // Unknown files are never auto-removed.
        foreach (var candidate in _plan.Candidates.Where(c => c.IsPreselected))
        {
            ToRemove.Add(new CleanRow(candidate));
        }

        Stage = CleanerStage.Warning;
        RaiseDerived();
    }

    private async Task<IReadOnlyList<string>> LoadKnownFilesAsync(CancellationToken cancellationToken)
    {
        var record = await _records.LoadAsync(cancellationToken).ConfigureAwait(true);
        return record?.Files.Select(f => f.RelativePath).ToList() ?? [];
    }

    /// <summary>Warning → Cleaning: moves the queued files to quarantine.</summary>
    [RelayCommand]
    private async Task ProceedAsync()
    {
        if (Stage != CleanerStage.Warning || !HasWork)
        {
            return;
        }

        Stage = CleanerStage.Cleaning;
        CleaningInProgress = true;
        CleanTotal = ToRemove.Count;
        CleanDone = 0;
        CleanProgress = 0;
        CleanCurrentItem = "Preparing to move files…";

        var paths = ToRemove.Select(r => r.Path).ToList();
        var gamePath = GamePath;

        // Created on the UI thread, so the callback marshals back here and can touch
        // bindable state directly. It names each file as it moves and advances the bar.
        var progress = new Progress<string>(relative =>
        {
            CleanDone++;
            CleanCurrentItem = $"Moving {relative}";
            CleanProgress = CleanTotal == 0 ? 1 : Math.Min(1.0, (double)CleanDone / CleanTotal);
            OnPropertyChanged(nameof(CleanCounter));
        });

        // Off the UI thread so the bar animates while the (possibly large) move runs.
        var batch = await Task.Run(() => _quarantine.QuarantineAsync(gamePath, paths, progress)).ConfigureAwait(true);

        // The files are safe in quarantine; now clear the empty folders the mods
        // created and left behind. Only empty folders go, so nothing that still
        // holds a file is touched.
        CleanCurrentItem = "Removing empty folders the mods left behind…";
        var foldersRemoved = await Task.Run(() =>
            new FolderCleaner(NullLogger<FolderCleaner>.Instance).PruneEmptyDirectories(gamePath)).ConfigureAwait(true);

        CleanProgress = 1;
        CleanCurrentItem = "Finished.";
        CleanSummary = foldersRemoved > 0
            ? $"Moved {batch.Entries.Count} file(s) to quarantine and cleared {foldersRemoved} empty mod folder(s) "
              + "the mods left behind. You can restore the files any time from Settings; the folders held nothing."
            : $"Moved {batch.Entries.Count} file(s) to quarantine. Nothing was deleted — "
              + "you can restore this batch any time from Settings.";
        CleaningInProgress = false;
    }

    /// <summary>Cleaning → Verify: shows the verify-your-files step.</summary>
    [RelayCommand]
    private void Continue()
    {
        if (Stage == CleanerStage.Cleaning && !CleaningInProgress)
        {
            Stage = CleanerStage.Verify;
            RaiseDerived();
        }
    }

    /// <summary>Closes the modal once verification is acknowledged.</summary>
    [RelayCommand]
    private void Close()
    {
        if (CanClose)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnStageChanged(CleanerStage value) => RaiseDerived();

    partial void OnVerifyAcknowledgedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(ShowVerifyHint));
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(RemoveCount));
        OnPropertyChanged(nameof(RemoveSize));
        OnPropertyChanged(nameof(ProtectedCount));
        OnPropertyChanged(nameof(HasProtected));
        OnPropertyChanged(nameof(HasWork));
        OnPropertyChanged(nameof(ShowCleanButton));
        OnPropertyChanged(nameof(ShowCloseWhenEmpty));
        OnPropertyChanged(nameof(VerifyPlatform));
        OnPropertyChanged(nameof(VerifySteps));
        OnPropertyChanged(nameof(StepCaption));
        OnPropertyChanged(nameof(ShowVerifyHint));
        OnPropertyChanged(nameof(CanClose));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024:0.0} MB",
    };
}
