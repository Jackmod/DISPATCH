using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Configuration;
using Dispatch.Core.Detection;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>One section of the settings hub.</summary>
/// <param name="Key">Stable identifier.</param>
/// <param name="Label">What the sub-nav shows.</param>
public sealed record HubSection(string Key, string Label);

/// <summary>One officer in the roster, with whether they are the one on duty.</summary>
/// <param name="Officer">The officer.</param>
/// <param name="IsActive">True when this is the officer currently on duty.</param>
public sealed record OfficerProfileRow(OfficerProfile Officer, bool IsActive);

/// <summary>One quarantine batch, shown in the restore list.</summary>
public sealed partial class QuarantineRow : ObservableObject
{
    /// <summary>Constructs a row over a batch.</summary>
    public QuarantineRow(QuarantineBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Batch = batch;
    }

    /// <summary>The batch.</summary>
    public QuarantineBatch Batch { get; }

    /// <summary>When it was made, in local time.</summary>
    public string When => Batch.CreatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>File count and total size.</summary>
    public string Summary => $"{Batch.Entries.Count} file(s) · {Bytes(Batch.TotalBytes)}";

    private static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>
/// The settings screen: appearance, officer identity with a one-click re-apply to
/// the config files, the folders Dispatch writes to, and quarantine restore.
/// </summary>
/// <remarks>
/// Editing the officer here is the visible half of the config engine: change a
/// callsign and "Re-apply to game" writes it back into every file that carries it —
/// Callout Interface, Grammar Police, the dash cam — through the same backed-up,
/// in-place writer the plugin-settings screen uses.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] Phonetics =
    [
        "ADAM", "BOY", "CHARLES", "DAVID", "EDWARD", "FRANK", "GEORGE", "HENRY", "IDA",
        "JOHN", "KING", "LINCOLN", "MARY", "NORA", "OCEAN", "PAUL", "QUEEN", "ROBERT",
        "SAM", "TOM", "UNION", "VICTOR", "WILLIAM", "XRAY", "YOUNG", "ZEBRA",
    ];

    private readonly IProfileStore _profiles;
    private readonly IAppPaths _paths;
    private readonly IQuarantine _quarantine;
    private readonly ISettingsWriter _settingsWriter;
    private readonly IUninstaller _uninstaller;
    private readonly ISystemDependencyProbe _dependencies;
    private readonly string? _gamePath;

    /// <summary>Raised when the user switches, adds or removes the active officer.</summary>
    public event EventHandler<OfficerProfile>? ActiveOfficerChanged;

    private DispatchProfile _profile = new();
    private OfficerProfile? _officer;
    private bool _loading;

    [ObservableProperty]
    private bool _reducedMotion;

    [ObservableProperty]
    private bool _soundEnabled;

    [ObservableProperty]
    private bool _discordPresence;

    [ObservableProperty]
    private string _officerName = string.Empty;

    [ObservableProperty]
    private Agency _agency = Agency.Lspd;

    [ObservableProperty]
    private int _callsignDivision = 1;

    [ObservableProperty]
    private string _callsignPhonetic = "ADAM";

    [ObservableProperty]
    private int _callsignBeat = 7;

    [ObservableProperty]
    private string _departmentName = string.Empty;

    [ObservableProperty]
    private string _airUnitCallsign = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Which section of the hub is showing.</summary>
    [ObservableProperty]
    private string _section = "keybinds";

    /// <summary>Constructs the screen. Services default for design-time and tests.</summary>
    public SettingsViewModel(
        OfficerProfile? officer = null,
        string? gamePath = null,
        IProfileStore? profiles = null,
        IAppPaths? paths = null,
        IQuarantine? quarantine = null,
        ISettingsWriter? settingsWriter = null,
        ISystemDependencyProbe? dependencies = null)
    {
        _officer = officer;
        _gamePath = gamePath;
        _paths = paths ?? new AppPaths();
        _profiles = profiles ?? new ProfileStore(_paths, NullLogger<ProfileStore>.Instance);
        _quarantine = quarantine ?? new Quarantine(_paths.QuarantineDirectory, NullLogger<Quarantine>.Instance);
        _settingsWriter = settingsWriter ?? new SettingsWriter();
        _dependencies = dependencies ?? new SystemDependencyProbe();
        _uninstaller = new Uninstaller(
            _paths,
            new InstallRecordStore(_paths, NullLogger<InstallRecordStore>.Instance),
            new BackupStore(_paths.BackupsDirectory, NullLogger<BackupStore>.Instance),
            _quarantine);

        // Settings is the hub: keybinds and plugin settings live inside it as their
        // own full screens rather than as separate rail destinations.
        Keybinds = new ControlsViewModel(gamePath: gamePath, officer: officer);
        PluginSettings = new PluginSettingsViewModel(gamePath: gamePath, officer: officer);
        CrashReport = new CrashReportViewModel(gamePath);

        if (officer is not null)
        {
            ApplyOfficer(officer);
        }

        Batches = [];
    }

    /// <summary>The keybinds editor, hosted as the first hub section.</summary>
    public ControlsViewModel Keybinds { get; }

    /// <summary>The plugin settings editor, hosted as the second hub section.</summary>
    public PluginSettingsViewModel PluginSettings { get; }

    /// <summary>The crash-report tool, hosted as a hub section.</summary>
    public CrashReportViewModel CrashReport { get; }

    // The three heavy sub-views (the keyboard map, the plugin list, the crash tool)
    // are built the first time their section is opened, then kept. Opening Settings
    // no longer constructs all three at once. The sub-VMs above hold the state, so a
    // view built on demand loses nothing, and once built it stays cached — switching
    // back is instant. Keybinds is the default section, so it counts as built up front.
    private bool _keybindsBuilt = true;
    private bool _pluginsBuilt;
    private bool _crashBuilt;

    /// <summary>The keybinds view-model once its section has been opened, else null.</summary>
    public object? KeybindsContent => _keybindsBuilt ? Keybinds : null;

    /// <summary>The plugin-settings view-model once its section has been opened, else null.</summary>
    public object? PluginSettingsContent => _pluginsBuilt ? PluginSettings : null;

    /// <summary>The crash-report view-model once its section has been opened, else null.</summary>
    public object? CrashContent => _crashBuilt ? CrashReport : null;

    /// <summary>The hub sections, in order.</summary>
    public IReadOnlyList<HubSection> Sections { get; } =
    [
        new("keybinds", "Keybinds"),
        new("plugins", "Plugin settings"),
        new("crash", "Crash report"),
        new("appearance", "Appearance"),
        new("officer", "Officer"),
        new("system", "System check"),
        new("folders", "Folders"),
        new("quarantine", "Quarantine"),
        new("uninstall", "Uninstall"),
        new("about", "About"),
    ];

    /// <summary>Whether the keybinds section is showing.</summary>
    public bool IsKeybinds => Section == "keybinds";

    /// <summary>Whether the plugin settings section is showing.</summary>
    public bool IsPlugins => Section == "plugins";

    /// <summary>Whether the crash-report section is showing.</summary>
    public bool IsCrash => Section == "crash";

    /// <summary>Whether the appearance section is showing.</summary>
    public bool IsAppearance => Section == "appearance";

    /// <summary>Whether the officer section is showing.</summary>
    public bool IsOfficer => Section == "officer";

    /// <summary>Whether the system-check section is showing.</summary>
    public bool IsSystem => Section == "system";

    /// <summary>Whether the folders section is showing.</summary>
    public bool IsFolders => Section == "folders";

    /// <summary>Whether the quarantine section is showing.</summary>
    public bool IsQuarantine => Section == "quarantine";

    /// <summary>Whether the uninstall section is showing.</summary>
    public bool IsUninstall => Section == "uninstall";

    /// <summary>Whether the about section is showing.</summary>
    public bool IsAbout => Section == "about";

    /// <summary>Switches the hub to a section.</summary>
    [RelayCommand]
    private void SetSection(string section) => Section = section;

    partial void OnSectionChanged(string value)
    {
        // Build a heavy sub-view the first time its section is opened; it stays cached after.
        if (value == "keybinds" && !_keybindsBuilt) { _keybindsBuilt = true; OnPropertyChanged(nameof(KeybindsContent)); }
        else if (value == "plugins" && !_pluginsBuilt) { _pluginsBuilt = true; OnPropertyChanged(nameof(PluginSettingsContent)); }
        else if (value == "crash" && !_crashBuilt) { _crashBuilt = true; OnPropertyChanged(nameof(CrashContent)); }

        OnPropertyChanged(nameof(IsKeybinds));
        OnPropertyChanged(nameof(IsPlugins));
        OnPropertyChanged(nameof(IsCrash));
        OnPropertyChanged(nameof(IsAppearance));
        OnPropertyChanged(nameof(IsOfficer));
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsFolders));
        OnPropertyChanged(nameof(IsQuarantine));
        OnPropertyChanged(nameof(IsUninstall));
        OnPropertyChanged(nameof(IsAbout));
    }

    // ===== System check ======================================

    /// <summary>The runtime dependencies and whether each was found.</summary>
    [ObservableProperty]
    private IReadOnlyList<DependencyStatus> _systemDependencies = [];

    /// <summary>Whether any required runtime is missing.</summary>
    public bool HasMissingDependencies => SystemDependencies.Any(d => d.IsMissing);

    /// <summary>Whether the check has run and found everything present.</summary>
    public bool AllDependenciesPresent =>
        SystemDependencies.Count > 0 && SystemDependencies.All(d => d.State == DependencyState.Installed);

    /// <summary>Opens a URL — a dependency download, or a mod's page — in the browser.</summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = $"Could not open {url}.";
        }
    }

    // ===== Officers ==========================================

    /// <summary>Every officer on file, for the switcher.</summary>
    public ObservableCollection<OfficerProfileRow> AllOfficers { get; } = [];

    /// <summary>Whether there is more than one officer to switch between.</summary>
    public bool HasMultipleOfficers => AllOfficers.Count > 1;

    /// <summary>Switches the active officer and re-applies their identity to the config files.</summary>
    [RelayCommand]
    private async Task SwitchOfficerAsync(OfficerProfile? officer)
    {
        if (officer is null || officer.Id == _profile.ActiveOfficerId)
        {
            return;
        }

        _profile = _profile with { ActiveOfficerId = officer.Id, UpdatedAt = DateTimeOffset.UtcNow };
        await _profiles.SaveAsync(_profile).ConfigureAwait(true);

        _officer = officer;
        ApplyOfficer(officer);
        RefreshOfficers();

        StatusMessage = await ReapplyIdentityAsync(officer).ConfigureAwait(true);
        ActiveOfficerChanged?.Invoke(this, officer);
    }

    /// <summary>Creates a new officer, makes them active, and opens the editor to fill in.</summary>
    [RelayCommand]
    private async Task AddOfficerAsync()
    {
        var officer = OfficerProfile.Create("New officer");
        _profile = _profile.WithOfficer(officer);
        await _profiles.SaveAsync(_profile).ConfigureAwait(true);

        _officer = officer;
        ApplyOfficer(officer);
        RefreshOfficers();

        Section = "officer";
        StatusMessage = "New officer created — fill in the details and save.";
        ActiveOfficerChanged?.Invoke(this, officer);
    }

    /// <summary>Removes an officer, promoting whoever is left. Never removes the last one.</summary>
    [RelayCommand]
    private async Task RemoveOfficerAsync(OfficerProfile? officer)
    {
        if (officer is null)
        {
            return;
        }

        if (_profile.Officers.Count <= 1)
        {
            StatusMessage = "You can't remove your only officer.";
            return;
        }

        _profile = _profile.WithoutOfficer(officer.Id);
        await _profiles.SaveAsync(_profile).ConfigureAwait(true);

        var active = _profile.ActiveOfficer;
        if (active is not null)
        {
            _officer = active;
            ApplyOfficer(active);
            ActiveOfficerChanged?.Invoke(this, active);
        }

        RefreshOfficers();
        StatusMessage = $"Removed {officer.Name}.";
    }

    private void RefreshOfficers()
    {
        AllOfficers.Clear();
        foreach (var officer in _profile.Officers)
        {
            AllOfficers.Add(new OfficerProfileRow(officer, officer.Id == _profile.ActiveOfficerId));
        }

        OnPropertyChanged(nameof(HasMultipleOfficers));
    }

    // ===== Uninstall =========================================

    /// <summary>Whether the "remove all data" confirmation is showing.</summary>
    [ObservableProperty]
    private bool _confirmingWipe;

    /// <summary>Whether the "return to stock" confirmation is showing.</summary>
    [ObservableProperty]
    private bool _confirmingStock;

    /// <summary>The result of the last uninstall action.</summary>
    [ObservableProperty]
    private string? _uninstallStatus;

    /// <summary>Asks for confirmation before wiping Dispatch's data.</summary>
    [RelayCommand]
    private void RequestWipe()
    {
        ConfirmingStock = false;
        ConfirmingWipe = true;
    }

    /// <summary>Backs out of the data wipe.</summary>
    [RelayCommand]
    private void CancelWipe() => ConfirmingWipe = false;

    /// <summary>
    /// Wipes every trace of Dispatch from the machine — profile, backups,
    /// quarantine, journals, logs, the mod pack, the OpenIV import folder and the
    /// Desktop shortcut. The game folder is not touched.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmWipeAsync()
    {
        ConfirmingWipe = false;
        var report = await _uninstaller.RemoveAppDataAsync().ConfigureAwait(true);
        UninstallStatus = report.Count == 0
            ? "Nothing to remove — Dispatch had no data on this machine."
            : $"Removed {report.Count} location(s), {Bytes(report.TotalBytes)} freed. "
              + "Your game folder was not touched. Close Dispatch to finish.";
    }

    /// <summary>Asks for confirmation before returning the game to stock.</summary>
    [RelayCommand]
    private void RequestStock()
    {
        ConfirmingWipe = false;
        ConfirmingStock = true;
    }

    /// <summary>Backs out of the return-to-stock.</summary>
    [RelayCommand]
    private void CancelStock() => ConfirmingStock = false;

    /// <summary>
    /// Returns the game folder to stock: restores every file Dispatch overwrote and
    /// moves every file it added into quarantine. Only files the install record
    /// names are touched, and only while they still match what Dispatch placed.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmStockAsync()
    {
        ConfirmingStock = false;

        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            UninstallStatus = "No game folder is set, so there is nothing to return to stock.";
            return;
        }

        var report = await _uninstaller.ReturnGameToStockAsync(_gamePath).ConfigureAwait(true);

        if (!report.DidAnything && report.Problems.Count == 0)
        {
            UninstallStatus = "Nothing installed — the game folder is already stock.";
            return;
        }

        var summary = $"Returned to stock: restored {report.FilesRestored} file(s), "
            + $"moved {report.FilesRemoved} mod file(s) to quarantine (restorable until you wipe Dispatch's data).";
        UninstallStatus = report.Problems.Count == 0
            ? summary
            : summary + $" {report.Problems.Count} file(s) needed attention: {report.Problems[0]}";

        await RefreshQuarantineAsync().ConfigureAwait(true);
    }

    private static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
        _ => $"{bytes} B",
    };

    /// <summary>The phonetic alphabet for the callsign picker.</summary>
    public IReadOnlyList<string> PhoneticOptions => Phonetics;

    /// <summary>The four agencies.</summary>
    public IReadOnlyList<Agency> AgencyOptions { get; } = [Agency.Lspd, Agency.Lssd, Agency.Sahp, Agency.Bcso];

    /// <summary>Division numbers, 1 to 10.</summary>
    public IReadOnlyList<int> DivisionOptions { get; } = Enumerable.Range(1, 10).ToList();

    /// <summary>Beat numbers, 1 to 24.</summary>
    public IReadOnlyList<int> BeatOptions { get; } = Enumerable.Range(1, 24).ToList();

    /// <summary>Quarantine batches available to restore, newest first.</summary>
    public ObservableCollection<QuarantineRow> Batches { get; }

    /// <summary>Whether there is anything in quarantine.</summary>
    public bool HasBatches => Batches.Count > 0;

    /// <summary>The live callsign preview.</summary>
    public string CallsignPreview => $"{CallsignDivision} {CallsignPhonetic} {CallsignBeat}";

    /// <summary>The app version, for the About section.</summary>
    public string Version =>
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

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

    /// <summary>Loads the profile and quarantine list. Called when the screen appears.</summary>
    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _profile = await _profiles.LoadAsync().ConfigureAwait(true);
            ReducedMotion = _profile.Appearance.ReducedMotion;
            SoundEnabled = _profile.Appearance.SoundEnabled;
            DiscordPresence = _profile.Appearance.DiscordPresence;

            _officer = _profile.ActiveOfficer ?? _officer;
            if (_officer is not null)
            {
                ApplyOfficer(_officer);
            }

            RefreshOfficers();
        }
        finally
        {
            _loading = false;
        }

        // The system check reads the machine, not the profile, so it runs outside the
        // loading guard and updates its own derived flags.
        SystemDependencies = _dependencies.Check();
        OnPropertyChanged(nameof(HasMissingDependencies));
        OnPropertyChanged(nameof(AllDependenciesPresent));

        await RefreshQuarantineAsync().ConfigureAwait(true);
    }

    partial void OnReducedMotionChanged(bool value) => _ = SaveAppearanceAsync();

    partial void OnSoundEnabledChanged(bool value) => _ = SaveAppearanceAsync();

    partial void OnDiscordPresenceChanged(bool value) => _ = SaveAppearanceAsync();

    partial void OnCallsignDivisionChanged(int value) => OnPropertyChanged(nameof(CallsignPreview));

    partial void OnCallsignPhoneticChanged(string value) => OnPropertyChanged(nameof(CallsignPreview));

    partial void OnCallsignBeatChanged(int value) => OnPropertyChanged(nameof(CallsignPreview));

    private async Task SaveAppearanceAsync()
    {
        if (_loading)
        {
            return;
        }

        _profile = _profile with
        {
            Appearance = _profile.Appearance with
            {
                ReducedMotion = ReducedMotion,
                SoundEnabled = SoundEnabled,
                DiscordPresence = DiscordPresence,
            },
        };

        await _profiles.SaveAsync(_profile).ConfigureAwait(true);
    }

    /// <summary>Saves the edited officer and writes the identity into the config files.</summary>
    [RelayCommand]
    private async Task SaveOfficerAsync()
    {
        var officer = (_officer ?? OfficerProfile.Create(OfficerName)) with
        {
            Name = string.IsNullOrWhiteSpace(OfficerName) ? "Officer" : OfficerName.Trim(),
            Agency = Agency,
            CallsignDivision = Math.Clamp(CallsignDivision, 1, 10),
            CallsignPhonetic = CallsignPhonetic,
            CallsignBeat = Math.Clamp(CallsignBeat, 1, 24),
            DepartmentName = string.IsNullOrWhiteSpace(DepartmentName) ? "Los Santos Police Department" : DepartmentName.Trim(),
            AirUnitCallsign = string.IsNullOrWhiteSpace(AirUnitCallsign) ? "AIR 1" : AirUnitCallsign.Trim(),
        };

        _officer = officer;
        _profile = _profile.WithOfficer(officer);
        await _profiles.SaveAsync(_profile).ConfigureAwait(true);

        StatusMessage = await ReapplyIdentityAsync(officer).ConfigureAwait(true);
    }

    private async Task<string> ReapplyIdentityAsync(OfficerProfile officer)
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            return "Officer saved. Set your game folder to write these into the config files.";
        }

        // Every catalogued setting that is personalised — callsign, agency, unit,
        // department — rewritten with the new identity, in each file's own form.
        var values = SettingsCatalogue.Settings
            .Where(s => s.Profile != ProfileField.None)
            .Select(s =>
            {
                var value = s.DefaultFor(officer);
                return new SettingValue(s, s.Quoted ? $"\"{value}\"" : value);
            })
            .ToList();

        var result = await _settingsWriter.WriteAsync(_gamePath, values).ConfigureAwait(true);

        if (result.IsEmpty && result.MissingFiles.Count == 0)
        {
            return "Officer saved. The config files already matched.";
        }

        var written = result.IsEmpty
            ? "Officer saved"
            : $"Officer saved and re-applied to {result.FilesTouched} config file(s), each backed up first";

        return result.MissingFiles.Count == 0
            ? written + "."
            : written + $". {result.MissingFiles.Count} file(s) do not exist yet and were skipped.";
    }

    /// <summary>Restores a quarantine batch to the game folder it came from.</summary>
    [RelayCommand]
    private async Task RestoreBatchAsync(QuarantineRow? row)
    {
        if (row is null)
        {
            return;
        }

        var failures = await _quarantine.RestoreAsync(row.Batch.Id).ConfigureAwait(true);
        StatusMessage = failures.Count == 0
            ? $"Restored {row.Batch.Entries.Count} file(s) from {row.When}."
            : $"Restored with {failures.Count} problem(s): {failures[0]}";

        await RefreshQuarantineAsync().ConfigureAwait(true);
    }

    /// <summary>Permanently deletes a quarantine batch.</summary>
    [RelayCommand]
    private async Task PurgeBatchAsync(QuarantineRow? row)
    {
        if (row is null)
        {
            return;
        }

        await _quarantine.PurgeAsync(row.Batch.Id).ConfigureAwait(true);
        StatusMessage = $"Purged the batch from {row.When}.";
        await RefreshQuarantineAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the game folder in the file browser.</summary>
    [RelayCommand]
    private void OpenGameFolder() => OpenFolder(_gamePath);

    /// <summary>Opens the logs folder.</summary>
    [RelayCommand]
    private void OpenLogs() => OpenFolder(_paths.LogsDirectory);

    /// <summary>Opens the backups folder.</summary>
    [RelayCommand]
    private void OpenBackups() => OpenFolder(_paths.BackupsDirectory);

    /// <summary>Opens the mod pack folder, where manual archives are dropped.</summary>
    [RelayCommand]
    private void OpenModPack() => OpenFolder(_paths.ModPackDirectory);

    /// <summary>Opens the OpenIV import folder.</summary>
    [RelayCommand]
    private void OpenImports() => OpenFolder(_paths.OpenIvImportDirectory);

    private async Task RefreshQuarantineAsync()
    {
        var batches = await _quarantine.ListBatchesAsync().ConfigureAwait(true);
        Batches.Clear();
        foreach (var batch in batches)
        {
            Batches.Add(new QuarantineRow(batch));
        }

        OnPropertyChanged(nameof(HasBatches));
    }

    private void ApplyOfficer(OfficerProfile officer)
    {
        OfficerName = officer.Name;
        Agency = officer.Agency;
        CallsignDivision = officer.CallsignDivision;
        CallsignPhonetic = officer.CallsignPhonetic;
        CallsignBeat = officer.CallsignBeat;
        DepartmentName = officer.DepartmentName;
        AirUnitCallsign = officer.AirUnitCallsign;
    }

    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "That folder is not set yet.";
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = $"Could not open {path}.";
        }
    }
}
