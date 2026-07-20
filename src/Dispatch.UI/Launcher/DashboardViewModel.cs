using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Controls;
using Dispatch.Core.Detection;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Dispatch.UI.Controls;
using Dispatch.UI.Imagery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>One tile in the dashboard status grid.</summary>
/// <param name="Eyebrow">Small uppercase label.</param>
/// <param name="Value">The headline value.</param>
/// <param name="Detail">A line of context beneath it.</param>
/// <param name="Tone">Status colour.</param>
public sealed record StatusTile(string Eyebrow, string Value, string Detail, StatusTone Tone);

/// <summary>
/// The dashboard: officer hero, a go-on-duty button, a grid of live status tiles,
/// a repair pass, and any problems translated out of the game logs.
/// </summary>
/// <remarks>
/// Everything reads from the install record, the game folder and the career log —
/// the tiles show what is really installed, Repair hashes every placed file against
/// the record to catch a launcher verification or an antivirus deletion, and the
/// crash-log reader turns RagePluginHook's own phrasing into "this plugin failed,
/// here is why" instead of a log file to squint at.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly Core.Platform.IGameLauncher? _launcher;
    private readonly IGameProcessGuard _guard;
    private readonly IInstallRecordStore _records;
    private readonly IVersionReader _versions;
    private readonly IProfileStatsStore _stats;
    private readonly IntegrityAuditor _auditor;
    private readonly GameLogReader _logReader;
    private readonly string? _gamePath;

    private InstallRecord? _record;
    private ProfileStats _career = new();

    /// <summary>Constructs the dashboard for an officer.</summary>
    public DashboardViewModel(
        OfficerProfile? officer = null,
        IGameBuildWatch? buildWatch = null,
        string? gamePath = null,
        Core.Platform.IGameLauncher? launcher = null,
        IInstallRecordStore? records = null,
        IVersionReader? versions = null,
        IProfileStatsStore? stats = null,
        IAppPaths? paths = null,
        IGameProcessGuard? guard = null)
    {
        Officer = officer;
        _launcher = launcher;
        _gamePath = gamePath;
        _guard = guard ?? new GameProcessGuard();

        var appPaths = paths ?? new AppPaths();
        _records = records ?? new InstallRecordStore(appPaths, NullLogger<InstallRecordStore>.Instance);
        _versions = versions ?? new VersionReader(NullLogger<VersionReader>.Instance);
        _stats = stats ?? new ProfileStatsStore(appPaths);
        _auditor = new IntegrityAuditor();
        _logReader = new GameLogReader();

        Tiles = BuildTiles();

        if (buildWatch is not null && !string.IsNullOrWhiteSpace(gamePath))
        {
            _ = CheckBuildAsync(buildWatch, gamePath);
        }
    }

    /// <summary>The live status tiles.</summary>
    [ObservableProperty]
    private IReadOnlyList<StatusTile> _tiles;

    /// <summary>The game-build comparison, once it has run. Null until then.</summary>
    [ObservableProperty]
    private ScriptHookStatus? _buildStatus;

    /// <summary>Whether an audit is running.</summary>
    [ObservableProperty]
    private bool _isAuditing;

    /// <summary>The last audit's verdict, or null before one has run.</summary>
    [ObservableProperty]
    private string? _auditVerdict;

    /// <summary>The last audit's findings.</summary>
    [ObservableProperty]
    private IReadOnlyList<AuditFinding> _auditFindings = [];

    /// <summary>Problems translated out of the game logs.</summary>
    [ObservableProperty]
    private IReadOnlyList<LogFinding> _logFindings = [];

    /// <summary>Whether an audit report is showing.</summary>
    public bool HasAudit => AuditFindings.Count > 0;

    /// <summary>Whether the crash-log reader found anything.</summary>
    public bool HasLogFindings => LogFindings.Count > 0;

    /// <summary>Whether to show the "your game updated" banner.</summary>
    public bool ShowUpdateAlert => BuildStatus?.NeedsUpdate == true;

    /// <summary>Alert headline.</summary>
    public string AlertHeadline => BuildStatus?.Headline ?? string.Empty;

    /// <summary>Alert explanation.</summary>
    public string AlertDetail => BuildStatus?.Detail ?? string.Empty;

    /// <summary>The officer on duty.</summary>
    public OfficerProfile? Officer { get; }

    /// <summary>Officer name, or a placeholder.</summary>
    public string OfficerName => Officer?.Name ?? "Officer";

    /// <summary>Callsign, or a placeholder.</summary>
    public string Callsign => Officer?.Callsign ?? "1 ADAM 7";

    /// <summary>Agency code.</summary>
    public string Agency => Officer?.AgencyCode ?? "LSPD";

    /// <summary>A backdrop photograph, if any are compiled in.</summary>
    public Avalonia.Media.IImage? Hero { get; } = ImageCatalog.For("dashboard", 0);

    /// <summary>True when there is a hero photograph.</summary>
    public bool HasHero => Hero is not null;

    /// <summary>Whether going on duty can actually launch anything.</summary>
    public bool CanGoOnDuty => _launcher?.IsAvailable == true && !string.IsNullOrWhiteSpace(_gamePath);

    private bool _loaded;

    /// <summary>Loads once, on first appearance; repeat visits are free.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Loads the real install record, career and any logged problems.</summary>
    public async Task LoadAsync()
    {
        _record = await _records.LoadAsync().ConfigureAwait(true);
        _career = await _stats.LoadAsync().ConfigureAwait(true);
        Tiles = BuildTiles();

        ReadCrashLogs();
    }

    /// <summary>
    /// Runs the integrity audit: hashes every placed file against the record and
    /// names anything a Rockstar update or antivirus removed.
    /// </summary>
    [RelayCommand]
    private async Task RunAuditAsync()
    {
        if (IsAuditing)
        {
            return;
        }

        IsAuditing = true;
        try
        {
            _record ??= await _records.LoadAsync().ConfigureAwait(true);

            if (_record is null || !_record.IsInstalled || string.IsNullOrWhiteSpace(_gamePath))
            {
                AuditFindings = [new AuditFinding(AuditSeverity.Ok, "Nothing installed yet",
                    "Run the installer, then Repair can verify every placed file.")];
                AuditVerdict = "Nothing to check";
            }
            else
            {
                var currentBuild = _versions.Read(_gamePath).GameBuild;
                var report = await _auditor.AuditAsync(_gamePath, _record, currentBuild).ConfigureAwait(true);
                AuditFindings = report.Findings;
                AuditVerdict = report.Verdict;
            }

            OnPropertyChanged(nameof(HasAudit));
            Tiles = BuildTiles();
        }
        finally
        {
            IsAuditing = false;
        }
    }

    /// <summary>The result of the last go-on-duty attempt, for the user to read. Null before one.</summary>
    [ObservableProperty]
    private string? _launchStatus;

    /// <summary>Whether there is a launch status to show.</summary>
    public bool HasLaunchStatus => !string.IsNullOrWhiteSpace(LaunchStatus);

    /// <summary>
    /// Starts RagePluginHook, which hooks GTA V and brings the plugins up, and sets
    /// <see cref="LaunchStatus"/> to exactly what happened so a failure is never silent.
    /// </summary>
    public void GoOnDuty()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            LaunchStatus = "No game folder is set. Point Dispatch at your GTA V folder in Settings, then try again.";
            return;
        }

        if (_launcher is null || !_launcher.IsAvailable)
        {
            LaunchStatus = "Launching is only available on Windows.";
            return;
        }

        // Don't start a second loader over a session that is already up.
        if (_guard.IsGameRunning(out var process))
        {
            LaunchStatus = $"{process ?? "The game"} is already running — you're on duty.";
            return;
        }

        LaunchStatus = _launcher.LaunchRagePluginHook(_gamePath) switch
        {
            Core.Platform.LaunchOutcome.Launched =>
                "RagePluginHook is starting. Hold Left Shift for the plugin list, tick your plugins, then Save and Launch.",
            Core.Platform.LaunchOutcome.LoaderNotFound =>
                "RagePluginHook.exe isn't in your game folder. Install LSPDFR (it ships RagePluginHook) and try again.",
            Core.Platform.LaunchOutcome.Unavailable =>
                "Launching is only available on Windows.",
            _ =>
                "RagePluginHook could not be started. Try launching it from the game folder by hand to see what it reports.",
        };
    }

    partial void OnLaunchStatusChanged(string? value) => OnPropertyChanged(nameof(HasLaunchStatus));

    private void ReadCrashLogs()
    {
        if (string.IsNullOrWhiteSpace(_gamePath))
        {
            return;
        }

        var findings = new List<LogFinding>();
        foreach (var name in new[] { "RagePluginHook.log", "LSPDFR.log" })
        {
            var path = Path.Combine(_gamePath, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                findings.AddRange(_logReader.Translate(File.ReadAllText(path)));
            }
            catch (IOException)
            {
                // A log held open by a running game is not worth failing over.
            }
        }

        LogFindings = findings;
        OnPropertyChanged(nameof(HasLogFindings));
    }

    private IReadOnlyList<StatusTile> BuildTiles()
    {
        var conflicts = ConflictDetector.Detect(ControlCatalogue.Bind(ControlCatalogue.Suggested)).Count;
        var tiles = new List<StatusTile>();

        if (_record is { IsInstalled: true } record)
        {
            tiles.Add(new StatusTile("GAME BUILD", record.GameBuild ?? "Unknown",
                BuildDetail(), BuildTone()));
            tiles.Add(new StatusTile("MODS", record.ModIds.Count.ToString(CultureInfo.InvariantCulture),
                $"{record.Files.Count} files placed", StatusTone.Neutral));
            tiles.Add(new StatusTile("INSTALLED",
                record.InstalledAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(record.PresetId) ? "Custom selection" : record.PresetId,
                StatusTone.Neutral));
        }
        else
        {
            tiles.Add(new StatusTile("SETUP", "Not installed", "Run the installer to begin", StatusTone.Neutral));
        }

        tiles.Add(new StatusTile("KEYBINDS",
            conflicts == 0 ? "No conflicts" : $"{conflicts} conflicts",
            "Suggested scheme",
            conflicts == 0 ? StatusTone.Good : StatusTone.Bad));

        if (_career.LastSession is { } last)
        {
            tiles.Add(new StatusTile("LAST SESSION", $"{(int)last.Minutes}m",
                $"{last.Callouts} callouts · {last.Arrests} arrests · {last.Pursuits} pursuits",
                StatusTone.Neutral));
        }
        else
        {
            tiles.Add(new StatusTile("CAREER", $"{_career.TotalHours}h",
                $"{_career.SessionCount} shift(s) on record", StatusTone.Neutral));
        }

        tiles.Add(new StatusTile("HEALTH",
            AuditVerdict ?? "Not checked",
            AuditVerdict is null ? "Run a repair check" : $"{AuditFindings.Count} finding(s)",
            AuditVerdict switch
            {
                "All good" => StatusTone.Good,
                "Problems found" => StatusTone.Bad,
                "Needs attention" => StatusTone.Warning,
                _ => StatusTone.Neutral,
            }));

        return tiles;
    }

    private string BuildDetail() => BuildStatus?.State switch
    {
        GameBuildState.GameUpdated => "Game updated — Script Hook outdated",
        GameBuildState.UpToDate => "Matches your install",
        _ => "Installed against this build",
    };

    private StatusTone BuildTone() => BuildStatus?.State switch
    {
        GameBuildState.GameUpdated => StatusTone.Bad,
        GameBuildState.UpToDate => StatusTone.Good,
        _ => StatusTone.Neutral,
    };

    private async Task CheckBuildAsync(IGameBuildWatch watch, string gamePath)
    {
        var status = await watch.CheckAsync(gamePath).ConfigureAwait(true);
        Dispatcher.UIThread.Post(() => BuildStatus = status);
    }

    partial void OnBuildStatusChanged(ScriptHookStatus? value)
    {
        OnPropertyChanged(nameof(ShowUpdateAlert));
        OnPropertyChanged(nameof(AlertHeadline));
        OnPropertyChanged(nameof(AlertDetail));
        Tiles = BuildTiles();
    }
}
