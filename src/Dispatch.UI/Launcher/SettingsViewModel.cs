using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Configuration;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>One section of the settings hub.</summary>
/// <param name="Key">Stable identifier.</param>
/// <param name="Label">What the sub-nav shows.</param>
public sealed record HubSection(string Key, string Label);

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
    private readonly string? _gamePath;

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
        ISettingsWriter? settingsWriter = null)
    {
        _officer = officer;
        _gamePath = gamePath;
        _paths = paths ?? new AppPaths();
        _profiles = profiles ?? new ProfileStore(_paths, NullLogger<ProfileStore>.Instance);
        _quarantine = quarantine ?? new Quarantine(_paths.QuarantineDirectory, NullLogger<Quarantine>.Instance);
        _settingsWriter = settingsWriter ?? new SettingsWriter();

        // Settings is the hub: keybinds and plugin settings live inside it as their
        // own full screens rather than as separate rail destinations.
        Keybinds = new ControlsViewModel(gamePath: gamePath, officer: officer);
        PluginSettings = new PluginSettingsViewModel(gamePath: gamePath, officer: officer);

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

    /// <summary>The hub sections, in order.</summary>
    public IReadOnlyList<HubSection> Sections { get; } =
    [
        new("keybinds", "Keybinds"),
        new("plugins", "Plugin settings"),
        new("appearance", "Appearance"),
        new("officer", "Officer"),
        new("folders", "Folders"),
        new("quarantine", "Quarantine"),
        new("about", "About"),
    ];

    /// <summary>Whether the keybinds section is showing.</summary>
    public bool IsKeybinds => Section == "keybinds";

    /// <summary>Whether the plugin settings section is showing.</summary>
    public bool IsPlugins => Section == "plugins";

    /// <summary>Whether the appearance section is showing.</summary>
    public bool IsAppearance => Section == "appearance";

    /// <summary>Whether the officer section is showing.</summary>
    public bool IsOfficer => Section == "officer";

    /// <summary>Whether the folders section is showing.</summary>
    public bool IsFolders => Section == "folders";

    /// <summary>Whether the quarantine section is showing.</summary>
    public bool IsQuarantine => Section == "quarantine";

    /// <summary>Whether the about section is showing.</summary>
    public bool IsAbout => Section == "about";

    /// <summary>Switches the hub to a section.</summary>
    [RelayCommand]
    private void SetSection(string section) => Section = section;

    partial void OnSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsKeybinds));
        OnPropertyChanged(nameof(IsPlugins));
        OnPropertyChanged(nameof(IsAppearance));
        OnPropertyChanged(nameof(IsOfficer));
        OnPropertyChanged(nameof(IsFolders));
        OnPropertyChanged(nameof(IsQuarantine));
        OnPropertyChanged(nameof(IsAbout));
    }

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
        }
        finally
        {
            _loading = false;
        }

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
