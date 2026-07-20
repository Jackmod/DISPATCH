using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>Where a mod stands relative to the game folder.</summary>
public enum ModState
{
    /// <summary>Installed and its files are present.</summary>
    Installed,

    /// <summary>Installed once, but its files are gone — a launcher verification or antivirus.</summary>
    Missing,

    /// <summary>In the catalogue but not installed.</summary>
    Available,
}

/// <summary>One mod in the list: what it is, where it came from, and what state it is in.</summary>
public sealed partial class ModRow : ObservableObject
{
    /// <summary>Constructs a row.</summary>
    public ModRow(string id, ModDefinition? definition, ModState state, IReadOnlyList<string> files)
    {
        Id = id;
        Definition = definition;
        State = state;
        Files = files;
    }

    /// <summary>The mod id.</summary>
    public string Id { get; }

    /// <summary>The catalogue definition, if known.</summary>
    public ModDefinition? Definition { get; }

    /// <summary>The files the install placed for this mod.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>What state it is in.</summary>
    public ModState State { get; }

    /// <summary>Whether the file list is expanded.</summary>
    [ObservableProperty]
    private bool _expanded;

    /// <summary>Display name.</summary>
    public string Name => Definition?.Name ?? Id;

    /// <summary>Author, or a dash.</summary>
    public string Author => string.IsNullOrWhiteSpace(Definition?.Author) ? "Unknown author" : Definition!.Author;

    /// <summary>The mod's page, if the catalogue has one.</summary>
    public string? Url => Definition?.Url;

    /// <summary>Whether there is a page to open.</summary>
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    /// <summary>How many files it placed.</summary>
    public int FileCount => Files.Count;

    /// <summary>A one-line status.</summary>
    public string StatusText => State switch
    {
        ModState.Installed => $"Installed · {FileCount} file(s)",
        ModState.Missing => "Files removed — restore or reinstall",
        _ => "Available to install",
    };

    /// <summary>Whether this mod's files can be removed to quarantine.</summary>
    public bool CanRemove => State == ModState.Installed && FileCount > 0;

    /// <summary>Whether there is a file list to show.</summary>
    public bool HasFiles => FileCount > 0;
}

/// <summary>
/// The mods list: everything installed, its state on disk, and reversible removal.
/// </summary>
/// <remarks>
/// State comes from checking the recorded files against the disk, not from the
/// record alone, so a mod whose files a launcher verification wiped reads as
/// "removed" rather than a phantom "installed". Removal moves a mod's files into
/// quarantine — the same reversible store the cleaner uses — so nothing is ever
/// destroyed, and it can be restored from Settings.
/// </remarks>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly IInstallRecordStore _records;
    private readonly IQuarantine _quarantine;
    private readonly string? _gamePath;

    private readonly List<ModRow> _all = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _filter = "All";

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Constructs the screen. Services default for design-time and tests.</summary>
    public ModsViewModel(
        string? gamePath = null,
        IInstallRecordStore? records = null,
        IQuarantine? quarantine = null,
        IAppPaths? paths = null)
    {
        _gamePath = gamePath;
        var appPaths = paths ?? new AppPaths();
        _records = records ?? new InstallRecordStore(appPaths, NullLogger<InstallRecordStore>.Instance);
        _quarantine = quarantine ?? new Quarantine(appPaths.QuarantineDirectory, NullLogger<Quarantine>.Instance);

        Rows = [];
    }

    /// <summary>The filtered rows.</summary>
    public ObservableCollection<ModRow> Rows { get; }

    /// <summary>The filter chips.</summary>
    public IReadOnlyList<string> Filters { get; } = ["All", "Installed", "Missing", "Available"];

    /// <summary>Whether there is anything to show at all.</summary>
    public bool IsEmpty => _all.Count == 0;

    /// <summary>How many mods are installed and present.</summary>
    public int InstalledCount => _all.Count(r => r.State == ModState.Installed);

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

    /// <summary>Loads the install record and builds the list, checking each file on disk.</summary>
    public async Task LoadAsync()
    {
        var record = await _records.LoadAsync().ConfigureAwait(true);
        _all.Clear();

        var installedIds = new HashSet<string>(record?.ModIds ?? [], StringComparer.OrdinalIgnoreCase);

        if (record is { IsInstalled: true })
        {
            foreach (var id in record.ModIds)
            {
                var files = record.Files.Where(f => string.Equals(f.Mod, id, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.RelativePath).ToList();
                var present = _gamePath is not null && files.Any(rel => File.Exists(FullPath(rel)));
                var state = files.Count > 0 && !present ? ModState.Missing : ModState.Installed;
                _all.Add(new ModRow(id, ModCatalogue.Mods.GetValueOrDefault(id), state, files));
            }
        }

        // Everything in the full setup that is not installed, as an "available" list
        // with a link to its page — so the screen is useful before an install too.
        foreach (var def in ModCatalogue.ModsFor("full-duty"))
        {
            if (!installedIds.Contains(def.Id))
            {
                _all.Add(new ModRow(def.Id, def, ModState.Available, []));
            }
        }

        Refresh();
    }

    /// <summary>Opens a mod's page in the browser.</summary>
    [RelayCommand]
    private void OpenPage(ModRow? row)
    {
        if (row?.Url is not { } url)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = "Could not open the page.";
        }
    }

    /// <summary>Shows a mod's placed files.</summary>
    [RelayCommand]
    private void ToggleFiles(ModRow? row)
    {
        if (row is not null)
        {
            row.Expanded = !row.Expanded;
        }
    }

    /// <summary>Moves a mod's files into quarantine — reversible from Settings.</summary>
    [RelayCommand]
    private async Task RemoveAsync(ModRow? row)
    {
        if (row is null || !row.CanRemove || string.IsNullOrWhiteSpace(_gamePath))
        {
            return;
        }

        var batch = await _quarantine.QuarantineAsync(_gamePath, row.Files).ConfigureAwait(true);
        StatusMessage = $"Moved {batch.Entries.Count} file(s) from {row.Name} to quarantine. Restore any time from Settings.";

        await LoadAsync().ConfigureAwait(true);
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        IEnumerable<ModRow> visible = _all;

        visible = Filter switch
        {
            "Installed" => visible.Where(r => r.State == ModState.Installed),
            "Missing" => visible.Where(r => r.State == ModState.Missing),
            "Available" => visible.Where(r => r.State == ModState.Available),
            _ => visible,
        };

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            visible = visible.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Author.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Id.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Rows.Clear();
        foreach (var row in visible.OrderBy(r => r.State).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(InstalledCount));
    }

    private string FullPath(string relative) =>
        Path.Combine(_gamePath!, relative.Replace('/', Path.DirectorySeparatorChar));
}
