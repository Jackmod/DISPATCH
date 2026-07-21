using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Maintenance;

namespace Dispatch.UI.Launcher;

/// <summary>One recognised cause from a pasted crash log.</summary>
public sealed class CrashFindingRow
{
    /// <summary>Builds a row from a translated finding.</summary>
    public CrashFindingRow(LogFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Title = finding.Title;
        Explanation = finding.Explanation;
    }

    /// <summary>What failed, named.</summary>
    public string Title { get; }

    /// <summary>Why, and what it means.</summary>
    public string Explanation { get; }
}

/// <summary>
/// The crash-report screen: paste a RagePluginHook or LSPDFR log and get the cause
/// in plain language instead of a wall of stack traces.
/// </summary>
/// <remarks>
/// It runs the same <see cref="GameLogReader"/> the dashboard uses to translate the
/// game's own logs, but over text the user pastes — so a crash on someone else's
/// machine, or a log copied from a Discord, can be diagnosed here too. When nothing
/// known matches, it says so and points at the most useful line to look at rather
/// than pretending to have an answer.
/// </remarks>
public sealed partial class CrashReportViewModel : ObservableObject
{
    private readonly GameLogReader _reader = new();
    private readonly string? _gamePath;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _analyzed;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Constructs the screen, optionally over a game folder to read logs from.</summary>
    public CrashReportViewModel(string? gamePath = null) => _gamePath = gamePath;

    /// <summary>The recognised causes from the last analysis.</summary>
    public ObservableCollection<CrashFindingRow> Findings { get; } = [];

    /// <summary>Whether any cause was recognised.</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>Whether an analysis ran over some text but matched nothing known.</summary>
    public bool NoKnownIssues => Analyzed && Findings.Count == 0 && !string.IsNullOrWhiteSpace(LogText);

    /// <summary>Whether the "read the log from my game folder" button applies.</summary>
    public bool CanLoadFromGame => !string.IsNullOrWhiteSpace(_gamePath);

    /// <summary>Whether there is anything to analyse or clear.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(LogText);

    /// <summary>Translates the pasted log into plain-language causes.</summary>
    [RelayCommand]
    private void Analyze()
    {
        Findings.Clear();

        if (!string.IsNullOrWhiteSpace(LogText))
        {
            foreach (var finding in _reader.Translate(LogText))
            {
                Findings.Add(new CrashFindingRow(finding));
            }
        }

        Analyzed = true;
        StatusMessage = Findings.Count switch
        {
            0 when string.IsNullOrWhiteSpace(LogText) => "Paste a RagePluginHook.log or LSPDFR.log to check it.",
            0 => "No known issue matched. Look for the plugin named just before the crash in the log, "
                 + "or paste more of it — the cause is usually the last plugin to load.",
            1 => "Found 1 likely cause.",
            var n => $"Found {n} likely causes.",
        };

        RaiseDerived();
    }

    /// <summary>Loads the logs straight from the game folder, then analyses them.</summary>
    [RelayCommand]
    private async Task LoadFromGameAsync()
    {
        if (!CanLoadFromGame || !Directory.Exists(_gamePath))
        {
            StatusMessage = "No game folder is set, so there are no logs to read.";
            return;
        }

        var builder = new StringBuilder();
        foreach (var name in new[] { "RagePluginHook.log", "LSPDFR.log" })
        {
            var path = Path.Combine(_gamePath!, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                builder.AppendLine($"===== {name} =====")
                       .AppendLine(await File.ReadAllTextAsync(path).ConfigureAwait(true));
            }
            catch (IOException)
            {
                // A log held open by a running game is not worth failing over.
            }
        }

        LogText = builder.ToString();
        if (string.IsNullOrWhiteSpace(LogText))
        {
            StatusMessage = "No RagePluginHook.log or LSPDFR.log found in the game folder yet.";
            return;
        }

        Analyze();
    }

    /// <summary>Clears the pasted log and any findings.</summary>
    [RelayCommand]
    private void Clear()
    {
        LogText = string.Empty;
        Findings.Clear();
        Analyzed = false;
        StatusMessage = null;
        RaiseDerived();
    }

    partial void OnLogTextChanged(string value) => OnPropertyChanged(nameof(HasText));

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(NoKnownIssues));
        OnPropertyChanged(nameof(HasText));
    }
}
