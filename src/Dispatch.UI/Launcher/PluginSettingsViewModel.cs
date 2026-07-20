using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using Dispatch.Core.Profiles;

namespace Dispatch.UI.Launcher;

/// <summary>One editable setting, holding a typed value for whichever editor it uses.</summary>
public sealed partial class SettingRow : ObservableObject
{
    private readonly Action _notify;
    private bool _live;
    private string _baseline = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private decimal? _numberValue;

    [ObservableProperty]
    private string _textValue = string.Empty;

    [ObservableProperty]
    private SettingOption? _selectedOption;

    [ObservableProperty]
    private bool _isChanged;

    /// <summary>Builds a row for a setting, seeded from its current raw value.</summary>
    public SettingRow(ModSetting setting, string currentRaw, Action notify)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(notify);

        Setting = setting;
        _notify = notify;

        SetFromRaw(currentRaw);
        _baseline = RawValue;
        Recompute();
        _live = true;
    }

    /// <summary>The setting this row edits.</summary>
    public ModSetting Setting { get; }

    /// <summary>True for a switch.</summary>
    public bool IsToggle => Setting.Kind == SettingKind.Toggle;

    /// <summary>True for a numeric field.</summary>
    public bool IsNumber => Setting.Kind == SettingKind.Number;

    /// <summary>True for a dropdown.</summary>
    public bool IsChoice => Setting.Kind == SettingKind.Choice;

    /// <summary>True for a free-text field.</summary>
    public bool IsText => Setting.Kind == SettingKind.Text;

    /// <summary>Lowest allowed number.</summary>
    public decimal Minimum => (decimal)Setting.Min;

    /// <summary>Highest allowed number.</summary>
    public decimal Maximum => (decimal)Setting.Max;

    /// <summary>Step between numbers.</summary>
    public decimal Increment => (decimal)Setting.Step;

    /// <summary>The named options, for a dropdown.</summary>
    public IReadOnlyList<SettingOption> Options => Setting.Options;

    /// <summary>Unit shown beside a numeric field, if any.</summary>
    public string? Unit => Setting.Unit;

    /// <summary>The value exactly as it will be written to the file.</summary>
    public string RawValue => Setting.Kind switch
    {
        SettingKind.Toggle => Setting.BoolToRaw(BoolValue),
        SettingKind.Number => FormatNumber(NumberValue),
        SettingKind.Choice => SelectedOption?.Value ?? Setting.Default,
        _ => FormatText(TextValue),
    };

    /// <summary>The current value in friendly words, for the staged-changes list.</summary>
    public string CurrentDisplay => Friendly(RawValue);

    /// <summary>A one-line summary of the pending change.</summary>
    public string ChangeSummary => $"{Setting.Name}: {Friendly(_baseline)} → {CurrentDisplay}";

    /// <summary>Accepts the current value as the new baseline.</summary>
    public void Commit()
    {
        _baseline = RawValue;
        Recompute();
    }

    /// <summary>Puts the value back to its baseline.</summary>
    public void Revert()
    {
        _live = false;
        SetFromRaw(_baseline);
        _live = true;
        Recompute();
    }

    /// <summary>Sets the value from a raw string as a live edit, staging the change.</summary>
    public void ApplyRaw(string? raw)
    {
        SetFromRaw(raw);
        Recompute();
        _notify();
    }

    /// <summary>Seeds the editor state from a raw file value.</summary>
    public void SetFromRaw(string? raw)
    {
        var wasLive = _live;
        _live = false;

        var value = ModSetting.Unquote(raw ?? string.Empty).Trim();

        switch (Setting.Kind)
        {
            case SettingKind.Toggle:
                BoolValue = Setting.ParseBool(raw);
                break;

            case SettingKind.Number:
                NumberValue = decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : ParseOrZero(Setting.Default);
                break;

            case SettingKind.Choice:
                SelectedOption =
                    Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))
                    ?? Options.FirstOrDefault(o => string.Equals(o.Value, Setting.Default, StringComparison.OrdinalIgnoreCase))
                    ?? Options.FirstOrDefault();
                break;

            default:
                TextValue = value;
                break;
        }

        _live = wasLive;
    }

    partial void OnBoolValueChanged(bool value) => OnEdited();

    partial void OnNumberValueChanged(decimal? value) => OnEdited();

    partial void OnTextValueChanged(string value) => OnEdited();

    partial void OnSelectedOptionChanged(SettingOption? value) => OnEdited();

    private void OnEdited()
    {
        if (!_live)
        {
            return;
        }

        Recompute();
        _notify();
    }

    private void Recompute()
    {
        IsChanged = !string.Equals(RawValue, _baseline, StringComparison.Ordinal);
        OnPropertyChanged(nameof(RawValue));
        OnPropertyChanged(nameof(CurrentDisplay));
        OnPropertyChanged(nameof(ChangeSummary));
    }

    private string Friendly(string raw) => Setting.Kind switch
    {
        SettingKind.Toggle => Setting.ParseBool(raw) ? "On" : "Off",
        SettingKind.Choice => Setting.ChoiceLabel(raw),
        SettingKind.Number => Unit is null ? raw : $"{raw} {Unit}",
        _ => ModSetting.Unquote(raw),
    };

    private string FormatNumber(decimal? value)
    {
        if (value is not { } number)
        {
            return Setting.Default;
        }

        return number == Math.Truncate(number)
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);
    }

    private string FormatText(string value) => Setting.Quoted ? $"\"{value}\"" : value;

    private static decimal ParseOrZero(string raw) =>
        decimal.TryParse(ModSetting.Unquote(raw), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}

/// <summary>A plugin's settings, grouped under its name.</summary>
/// <param name="Plugin">The plugin name.</param>
/// <param name="Rows">Its settings, in display order.</param>
public sealed record SettingGroup(string Plugin, IReadOnlyList<SettingRow> Rows);

/// <summary>
/// The plugin settings screen: every mod config value in an editable, searchable
/// form, staged and written back to the real ini files.
/// </summary>
/// <remarks>
/// This is the whole of a manual install's thirty-config-file step turned into
/// switches, sliders and dropdowns. Nothing writes until Apply, every write backs
/// its file up first, and each control reads and writes the mod's own literal, so
/// the file the game reads is exactly what it expects.
/// </remarks>
public sealed partial class PluginSettingsViewModel : ObservableObject
{
    private const string AllPlugins = "All plugins";

    private readonly List<SettingRow> _all = [];
    private readonly ISettingsWriter _writer;
    private readonly IIniScanner _scanner;
    private readonly string? _gamePath;

    // File|key of everything the catalogues already own, so a scan never lists a
    // setting or keybind the app already presents in a nicer form.
    private readonly HashSet<string> _curatedKeys;

    // File|section|key of discovered settings already added, so a re-scan does not
    // duplicate them.
    private readonly HashSet<string> _discoveredKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _pluginFilter = AllPlugins;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Constructs the screen. Dependencies are optional for design-time and tests.</summary>
    public PluginSettingsViewModel(
        string? gamePath = null,
        ISettingsWriter? writer = null,
        OfficerProfile? officer = null,
        IIniScanner? scanner = null)
    {
        _gamePath = gamePath;
        _writer = writer ?? new SettingsWriter();
        _scanner = scanner ?? new IniScanner();

        foreach (var setting in SettingsCatalogue.Settings)
        {
            _all.Add(new SettingRow(setting, setting.DefaultFor(officer), OnRowChanged));
        }

        // A scan defers to anything already curated — as a setting here, or as a
        // keybind on the controls screen — so it only ever adds what is new.
        _curatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in SettingsCatalogue.Settings)
        {
            _curatedKeys.Add($"{setting.ConfigFile}|{setting.ConfigKey}");
        }

        foreach (var action in ControlCatalogue.Actions)
        {
            _curatedKeys.Add($"{action.ConfigFile}|{action.ConfigKey}");
        }

        Groups = [];
        Refresh();
    }

    /// <summary>The filtered settings, grouped by plugin.</summary>
    public ObservableCollection<SettingGroup> Groups { get; }

    /// <summary>The plugin filter options, "All plugins" first, including scanned mods.</summary>
    public IReadOnlyList<string> PluginFilters =>
        new[] { AllPlugins }
            .Concat(_all.Select(row => row.Setting.Plugin).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            .ToList();

    /// <summary>How many settings differ from their baseline.</summary>
    public int PendingCount => _all.Count(row => row.IsChanged);

    /// <summary>True when there is anything to apply.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>The staged changes, in friendly words.</summary>
    public IReadOnlyList<string> PendingDiff =>
        _all.Where(row => row.IsChanged)
            .OrderBy(row => row.Setting.Plugin, StringComparer.Ordinal)
            .ThenBy(row => row.Setting.Name, StringComparer.Ordinal)
            .Select(row => row.ChangeSummary)
            .ToList();

    private bool _loaded;

    /// <summary>Loads once, on first appearance; repeat visits are free.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadFromDiskAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the current values out of the game folder, making them the baseline.
    /// Called when the screen appears and by the read-back command.
    /// </summary>
    public async Task LoadFromDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            return;
        }

        var onDisk = await _writer.ReadAsync(_gamePath, SettingsCatalogue.Settings).ConfigureAwait(true);

        foreach (var row in _all)
        {
            if (onDisk.TryGetValue(row.Setting.Id, out var raw))
            {
                row.SetFromRaw(raw);
                row.Commit();
            }
        }

        // Also sweep the folder for anything not in the catalogue, so a plugin the
        // user dropped in shows up without them having to press Scan.
        var found = await ScanInternalAsync().ConfigureAwait(true);

        StatusMessage = onDisk.Count == 0 && found == 0
            ? "No config files found yet — showing the recommended defaults."
            : $"Loaded {onDisk.Count} catalogued value(s) and found {found} more by scanning your plugins.";

        Refresh();
    }

    /// <summary>Re-scans the mod folders for settings the catalogue does not cover.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            StatusMessage = "No game folder is set, so there is nothing to scan.";
            return;
        }

        var added = await ScanInternalAsync().ConfigureAwait(true);

        StatusMessage = added == 0
            ? "Scan complete — no new settings found beyond what is already listed."
            : $"Scan complete — added {added} newly-discovered setting(s). Add a mod and scan again any time.";

        Refresh();
    }

    /// <summary>
    /// Runs a scan and folds any never-seen settings in as new rows. Returns how
    /// many were added.
    /// </summary>
    private async Task<int> ScanInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            return 0;
        }

        var discovered = await _scanner.ScanAsync(_gamePath).ConfigureAwait(true);
        var added = 0;

        foreach (var setting in discovered)
        {
            // A key the catalogue already owns is shown in its nicer curated form.
            if (_curatedKeys.Contains($"{setting.ConfigFile}|{setting.ConfigKey}"))
            {
                continue;
            }

            // A key already discovered on an earlier scan is not added twice.
            if (!_discoveredKeys.Add($"{setting.ConfigFile}|{setting.Section}|{setting.ConfigKey}"))
            {
                continue;
            }

            _all.Add(new SettingRow(setting, setting.Default, OnRowChanged));
            added++;
        }

        return added;
    }

    /// <summary>Writes every staged change into its config file, backing each up first.</summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!string.IsNullOrWhiteSpace(_gamePath) && Directory.Exists(_gamePath))
        {
            var values = _all
                .Where(row => row.IsChanged)
                .Select(row => new SettingValue(row.Setting, row.RawValue))
                .ToList();

            var result = await _writer.WriteAsync(_gamePath, values).ConfigureAwait(true);
            StatusMessage = Describe(result);
        }
        else
        {
            StatusMessage = "No game folder is set, so the changes are kept in the app only.";
        }

        foreach (var row in _all)
        {
            row.Commit();
        }

        Refresh();
    }

    /// <summary>Re-reads the config files, discarding staged edits in favour of what is on disk.</summary>
    [RelayCommand]
    private async Task ReadFromGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            StatusMessage = "No game folder is set, so there is nothing to read.";
            return;
        }

        await LoadFromDiskAsync().ConfigureAwait(true);
    }

    /// <summary>Discards every staged change.</summary>
    [RelayCommand]
    private void Discard()
    {
        foreach (var row in _all)
        {
            row.Revert();
        }

        Refresh();
    }

    /// <summary>Resets one setting to the recommended default.</summary>
    [RelayCommand]
    private void ResetRow(SettingRow? row)
    {
        if (row is null)
        {
            return;
        }

        // Stage the default so the user sees it and applies it like any other edit.
        row.ApplyRaw(row.Setting.Default);
        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnPluginFilterChanged(string value) => Refresh();

    private void OnRowChanged()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingDiff));
    }

    private static string Describe(SettingsWriteResult result)
    {
        if (result.IsEmpty && result.MissingFiles.Count == 0)
        {
            return "Nothing to write — the config files already match.";
        }

        var written = result.IsEmpty
            ? "No values changed"
            : $"Wrote {result.Changes.Count} value(s) across {result.FilesTouched} file(s); each was backed up first";

        return result.MissingFiles.Count == 0
            ? written + "."
            : written + $". {result.MissingFiles.Count} file(s) do not exist yet and were skipped — some mods write their config on first launch.";
    }

    private void Refresh()
    {
        var visible = _all.AsEnumerable();

        if (PluginFilter != AllPlugins)
        {
            visible = visible.Where(row => row.Setting.Plugin == PluginFilter);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            visible = visible.Where(row =>
                row.Setting.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Setting.Plugin.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Setting.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Setting.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Setting.ConfigKey.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Setting.ConfigFile.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var groups = visible
            .GroupBy(row => row.Setting.Plugin, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SettingGroup(
                group.Key,
                group.OrderBy(row => row.Setting.Category, StringComparer.Ordinal)
                     .ThenBy(row => row.Setting.Name, StringComparer.Ordinal)
                     .ToList()));

        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingDiff));
        OnPropertyChanged(nameof(PluginFilters));
    }
}
