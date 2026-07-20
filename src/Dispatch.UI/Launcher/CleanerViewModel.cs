using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>A single row in the cleaner preview, with its own checkbox.</summary>
public sealed partial class CleanRow : ObservableObject
{
    [ObservableProperty]
    private bool _selected;

    /// <summary>Constructs a row from a scanned candidate.</summary>
    public CleanRow(CleanCandidate candidate)
    {
        Candidate = candidate;
        _selected = candidate.IsPreselected;
    }

    /// <summary>The candidate this row represents.</summary>
    public CleanCandidate Candidate { get; }

    /// <summary>Its path.</summary>
    public string Path => Candidate.RelativePath;

    /// <summary>Its size, formatted.</summary>
    public string Size => FormatSize(Candidate.SizeBytes);

    /// <summary>Why the scanner classified it this way.</summary>
    public string Reason => Candidate.Reason;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024:0.0} MB",
    };
}

/// <summary>One tier group in the preview tree.</summary>
public sealed partial class CleanTier_Group : ObservableObject
{
    /// <summary>Constructs a tier group.</summary>
    public CleanTier_Group(CleanTier tier, string title, string caution, IEnumerable<CleanRow> rows)
    {
        Tier = tier;
        Title = title;
        Caution = caution;
        Rows = new ObservableCollection<CleanRow>(rows);
    }

    /// <summary>Which tier.</summary>
    public CleanTier Tier { get; }

    /// <summary>The heading.</summary>
    public string Title { get; }

    /// <summary>A caution line, empty for the safe tier.</summary>
    public string Caution { get; }

    /// <summary>Whether the caution line shows.</summary>
    public bool HasCaution => !string.IsNullOrEmpty(Caution);

    /// <summary>The rows in this tier.</summary>
    public ObservableCollection<CleanRow> Rows { get; }

    /// <summary>Whether this group has any rows to show.</summary>
    public bool HasRows => Rows.Count > 0;
}

/// <summary>Where the cleaner is in its flow.</summary>
public enum CleanerStage
{
    /// <summary>Not yet started.</summary>
    Idle,

    /// <summary>Scanning the folder.</summary>
    Scanning,

    /// <summary>Showing the preview, waiting for confirmation.</summary>
    Preview,

    /// <summary>Moving files to quarantine.</summary>
    Cleaning,

    /// <summary>Done, showing the summary.</summary>
    Done,
}

/// <summary>
/// Drives the Clean GTA folder modal: scan, preview, confirm, quarantine.
/// </summary>
/// <remarks>
/// The confirm button is gated on the tree having been scrolled to the bottom,
/// as the spec insists — the point is to make the user actually look at what
/// they are about to remove rather than clicking through. Nothing is moved until
/// they have both scrolled and confirmed, and even then it goes to quarantine,
/// not the bin.
/// </remarks>
public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly FolderCleaner _cleaner;
    private readonly IQuarantine _quarantine;

    [ObservableProperty]
    private CleanerStage _stage = CleanerStage.Idle;

    [ObservableProperty]
    private int _filesScanned;

    [ObservableProperty]
    private bool _scrolledToBottom;

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Constructs the cleaner view model.</summary>
    public CleanerViewModel(FolderCleaner? cleaner = null, IQuarantine? quarantine = null)
    {
        _cleaner = cleaner ?? new FolderCleaner(NullLogger<FolderCleaner>.Instance);
        _quarantine = quarantine ?? new Quarantine(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dispatch-quarantine-fallback"),
            NullLogger<Quarantine>.Instance);

        Tiers = [];
    }

    /// <summary>The preview tiers.</summary>
    public ObservableCollection<CleanTier_Group> Tiers { get; }

    /// <summary>How many rows are selected across every tier.</summary>
    public int SelectedCount => Tiers.SelectMany(t => t.Rows).Count(r => r.Selected);

    /// <summary>Total size of the selected rows.</summary>
    public string SelectedSize => FormatSize(
        Tiers.SelectMany(t => t.Rows).Where(r => r.Selected).Sum(r => r.Candidate.SizeBytes));

    /// <summary>Whether the confirm button is enabled.</summary>
    /// <remarks>
    /// Both conditions: the user has scrolled to the bottom of the tree, and at
    /// least one row is selected. The scroll gate is what makes them look.
    /// </remarks>
    public bool CanConfirm => Stage == CleanerStage.Preview && ScrolledToBottom && SelectedCount > 0;

    /// <summary>Starts a scan of the given folder.</summary>
    public async Task ScanAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        GamePath = gamePath;
        Stage = CleanerStage.Scanning;
        FilesScanned = 0;
        Tiers.Clear();

        var progress = new Progress<int>(count => Dispatcher.UIThread.Post(() => FilesScanned = count));

        var plan = await Task.Run(() => _cleaner.Scan(gamePath, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        BuildTiers(plan);
        Stage = CleanerStage.Preview;
        RaiseDerived();
    }

    /// <summary>Called when a row's checkbox toggles, to refresh the totals.</summary>
    public void OnSelectionChanged() => RaiseDerived();

    /// <summary>Called when the tree is scrolled to the bottom.</summary>
    [RelayCommand]
    private void MarkScrolledToBottom()
    {
        ScrolledToBottom = true;
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>Moves the selected files to quarantine.</summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!CanConfirm)
        {
            return;
        }

        Stage = CleanerStage.Cleaning;

        var selected = Tiers.SelectMany(t => t.Rows)
            .Where(r => r.Selected)
            .Select(r => r.Path)
            .ToList();

        var batch = await _quarantine.QuarantineAsync(GamePath, selected).ConfigureAwait(true);

        Summary = $"Moved {batch.Entries.Count} file(s) to quarantine. " +
                  "Nothing was deleted — you can restore this batch in one click from Settings.";
        Stage = CleanerStage.Done;
    }

    partial void OnScrolledToBottomChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    private void BuildTiers(CleanPlan plan)
    {
        void AddTier(CleanTier tier, string title, string caution)
        {
            var rows = plan.Candidates
                .Where(c => c.Tier == tier)
                .Select(c => new CleanRow(c))
                .ToList();

            foreach (var row in rows)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanRow.Selected))
                    {
                        OnSelectionChanged();
                    }
                };
            }

            Tiers.Add(new CleanTier_Group(tier, title, caution, rows));
        }

        AddTier(CleanTier.Known, "Known mod files", string.Empty);
        AddTier(CleanTier.Likely, "Likely mod files", "Matches a mod pattern but is not recognised. Check before removing.");
        AddTier(CleanTier.Unknown, "Unknown", "Not recognised at all. Selected only if you decide to.");
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024:0.0} MB",
    };
}
