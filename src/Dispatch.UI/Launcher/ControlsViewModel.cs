using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using Dispatch.Core.Profiles;

namespace Dispatch.UI.Launcher;

/// <summary>One row in the action list.</summary>
public sealed partial class BindingRow : ObservableObject
{
    [ObservableProperty]
    private KeyBinding _binding;

    [ObservableProperty]
    private bool _isChanged;

    [ObservableProperty]
    private bool _isConflicted;

    /// <summary>Constructs a row.</summary>
    public BindingRow(GameAction action, KeyBinding binding)
    {
        Action = action;
        _binding = binding;
        Original = binding;
    }

    /// <summary>The action this row edits.</summary>
    public GameAction Action { get; }

    /// <summary>What it was bound to when the profile was loaded.</summary>
    public KeyBinding Original { get; private set; }

    /// <summary>The binding in plain words.</summary>
    public string BindingDisplay => Binding.Display;

    /// <summary>True when this key needs Num Lock to register.</summary>
    public bool NeedsNumLock => Binding.Key.IsNumpad;

    /// <summary>Accepts the current binding as the new baseline.</summary>
    public void Commit()
    {
        Original = Binding;
        IsChanged = false;
    }

    /// <summary>Puts the binding back to its baseline.</summary>
    public void Revert()
    {
        Binding = Original;
        IsChanged = false;
    }

    partial void OnBindingChanged(KeyBinding value)
    {
        IsChanged = value != Original;
        OnPropertyChanged(nameof(BindingDisplay));
        OnPropertyChanged(nameof(NeedsNumLock));
    }
}

/// <summary>
/// The controls screen: the keyboard map, the action list, and the staged
/// changes between them.
/// </summary>
/// <remarks>
/// Edits stage rather than write. Nothing reaches a config file until Apply,
/// and Apply shows the diff first — which is what makes this safe to explore
/// rather than something to be careful with.
/// </remarks>
public sealed partial class ControlsViewModel : ObservableObject
{
    private readonly List<BindingRow> _all = [];
    private readonly IControlWriter _writer;
    private readonly IIniScanner _scanner;
    private readonly string? _gamePath;
    private readonly OfficerProfile? _officer;

    // File|key of every keybind discovered by scanning, so a re-scan never adds the
    // same bind twice.
    private readonly HashSet<string> _discoveredKeybindKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private KeyModifier _layer;

    [ObservableProperty]
    private bool _freeKeysOnly;

    [ObservableProperty]
    private bool _conflictsOnly;

    [ObservableProperty]
    private KeyToken? _selectedKey;

    [ObservableProperty]
    private BindingRow? _capturing;

    [ObservableProperty]
    private string _profileName = "Suggested";

    [ObservableProperty]
    private InputDevice _device = InputDevice.Keyboard;

    [ObservableProperty]
    private string _pluginFilter = AllPlugins;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _keyPanelSearch = string.Empty;

    [ObservableProperty]
    private bool _showConflictsPanel;

    [ObservableProperty]
    private bool _showApplyPreview;

    private const string AllPlugins = "All plugins";

    /// <summary>
    /// Constructs the screen. Every dependency is optional, so the design-time and
    /// test cases stay a bare <c>new()</c> against the Suggested scheme, while the
    /// launcher supplies the game folder, writer and officer that make Apply, the
    /// read-back and the cheat sheet real.
    /// </summary>
    public ControlsViewModel(
        IReadOnlyDictionary<string, KeyBinding>? scheme = null,
        string? gamePath = null,
        IControlWriter? writer = null,
        OfficerProfile? officer = null,
        IIniScanner? scanner = null)
    {
        _gamePath = gamePath;
        _writer = writer ?? new ControlWriter();
        _scanner = scanner ?? new IniScanner();
        _officer = officer;

        foreach (var bound in ControlCatalogue.Bind(scheme ?? ControlCatalogue.Suggested))
        {
            _all.Add(new BindingRow(bound.Action, bound.Binding));
        }

        Rows = [];
        Refresh();
    }

    /// <summary>The rows currently shown, after search and filters.</summary>
    public ObservableCollection<BindingRow> Rows { get; }

    /// <summary>The schemes offered in the profile picker.</summary>
    public IReadOnlyList<string> ProfileNames { get; } = ["Default", "Suggested", "Custom"];

    /// <summary>The plugin filter options, "All plugins" first, including discovered mods.</summary>
    public IReadOnlyList<string> PluginFilters =>
        new[] { AllPlugins }
            .Concat(_all.Select(row => row.Action.Plugin).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            .ToList();

    /// <summary>Every binding, for the keyboard map.</summary>
    public IReadOnlyList<BoundAction> AllBindings =>
        _all.Select(row => new BoundAction(row.Action, row.Binding)).ToList();

    /// <summary>The keyboard map only makes sense on the keyboard tab.</summary>
    public bool ShowKeyboardMap => Device == InputDevice.Keyboard;

    /// <summary>Whether the click-a-key mapping panel is open.</summary>
    public bool IsKeyPanelOpen => SelectedKey is not null;

    /// <summary>The selected key in plain words, for the panel header.</summary>
    public string SelectedKeyDisplay =>
        SelectedKey is { } key ? KeyTokens.ToDisplay(key) : string.Empty;

    /// <summary>The modifier the current layer maps onto — shown so a chord is obvious.</summary>
    public string LayerLabel => Layer switch
    {
        KeyModifier.Shift => "Left Shift + ",
        KeyModifier.Control => "Left Control + ",
        KeyModifier.Alt => "Left Alt + ",
        _ => string.Empty,
    };

    /// <summary>Everything already bound to the selected key on the current layer.</summary>
    public IReadOnlyList<BindingRow> ActionsOnSelectedKey =>
        SelectedKey is { } key
            ? _all.Where(row => row.Action.Device == Device
                    && !row.Binding.IsUnbound
                    && row.Binding.Key == key
                    && row.Binding.Modifier == Layer)
                .OrderBy(row => row.Action.Plugin, StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>Whether anything is already on the selected key.</summary>
    public bool SelectedKeyHasActions => ActionsOnSelectedKey.Count > 0;

    /// <summary>
    /// Every action that could be mapped to the selected key — the panel's list.
    /// Anything already on this exact key and layer is left out, and the panel's
    /// own search narrows it.
    /// </summary>
    public IReadOnlyList<BindingRow> AssignableActions
    {
        get
        {
            if (SelectedKey is not { } key)
            {
                return [];
            }

            var term = KeyPanelSearch.Trim();
            return _all
                .Where(row => row.Action.Device == Device)
                .Where(row => !(row.Binding.Key == key && row.Binding.Modifier == Layer))
                .Where(row => string.IsNullOrEmpty(term)
                    || row.Action.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Action.Plugin.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Action.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.Action.Plugin, StringComparer.Ordinal)
                .ThenBy(row => row.Action.Name, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Conflicts across the whole scheme, not just the visible rows.</summary>
    public IReadOnlyList<Conflict> Conflicts => ConflictDetector.Detect(AllBindings);

    /// <summary>How many rows differ from the loaded profile.</summary>
    public int PendingCount => _all.Count(row => row.IsChanged);

    /// <summary>True when there is anything to apply.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>Label for the conflict chip.</summary>
    public string ConflictSummary => Conflicts.Count switch
    {
        0 => "No conflicts",
        1 => "1 conflict",
        var n => $"{n} conflicts",
    };

    /// <summary>True when the scheme is clean.</summary>
    public bool IsClean => Conflicts.Count == 0;

    /// <summary>The staged diff, as it will be shown before applying.</summary>
    public IReadOnlyList<string> PendingDiff =>
        _all.Where(row => row.IsChanged)
            .OrderBy(row => row.Action.ConfigFile, StringComparer.Ordinal)
            .Select(row =>
                $"{row.Action.ConfigFile}\n  {row.Action.ConfigKey,-32} {row.Original.Display}  →  {row.Binding.Display}")
            .ToList();

    /// <summary>Switches the keyboard map to a modifier layer.</summary>
    [RelayCommand]
    private void SetLayer(KeyModifier layer) => Layer = layer;

    /// <summary>Switches between the keyboard and controller tabs.</summary>
    [RelayCommand]
    private void SetDevice(InputDevice device) => Device = device;

    /// <summary>Which controller art the map draws: <c>Xbox</c> or <c>PS</c>.</summary>
    [ObservableProperty]
    private string _padType = "Xbox";

    /// <summary>Switches the controller art.</summary>
    [RelayCommand]
    private void SetPadType(string type) => PadType = type;

    /// <summary>Starts capture on a row. The next key pressed becomes its binding.</summary>
    [RelayCommand]
    private void BeginCapture(BindingRow? row) => Capturing = row;

    /// <summary>Cancels capture without changing anything.</summary>
    [RelayCommand]
    private void CancelCapture() => Capturing = null;

    /// <summary>
    /// Maps an action onto the selected key (with the current layer's modifier),
    /// from the click-a-key panel. This is the direct answer to "what can I put on
    /// this key?" — pick from every action, and it moves here.
    /// </summary>
    [RelayCommand]
    private void MapToKey(BindingRow? row)
    {
        if (row is null || SelectedKey is not { } key)
        {
            return;
        }

        row.Binding = new KeyBinding(key, Layer);
        Refresh();
    }

    /// <summary>Closes the click-a-key panel.</summary>
    [RelayCommand]
    private void ClosePanel() => SelectedKey = null;

    /// <summary>Resets one binding to the Suggested (conflict-free) default.</summary>
    [RelayCommand]
    private void ResetRow(BindingRow? row)
    {
        if (row is null)
        {
            return;
        }

        // A discovered bind has no suggested default, so "reset" puts it back to the
        // value it was loaded with from disk.
        row.Binding = ControlCatalogue.Suggested.TryGetValue(row.Action.Id, out var binding)
            ? binding
            : row.Original;
        Refresh();
    }

    /// <summary>Clears a row's binding.</summary>
    [RelayCommand]
    private void Unbind(BindingRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Binding = KeyBinding.Unbound;
        Capturing = null;
        Refresh();
    }

    /// <summary>Applies a captured key to the row being captured.</summary>
    public void ApplyCapture(KeyToken key, KeyModifier modifier)
    {
        if (Capturing is null)
        {
            return;
        }

        Capturing.Binding = new KeyBinding(key, modifier);
        Capturing = null;
        Refresh();
    }

    /// <summary>Swaps two conflicting actions' bindings.</summary>
    [RelayCommand]
    private void ResolveBySwap(Conflict? conflict)
    {
        if (conflict is null || conflict.Actions.Count < 2)
        {
            return;
        }

        var first = _all.First(row => row.Action.Id == conflict.Actions[0].Id);
        var second = _all.First(row => row.Action.Id == conflict.Actions[1].Id);

        (first.Binding, second.Binding) = (second.Binding, first.Binding);
        Refresh();
    }

    /// <summary>Moves everything after the first claimant onto free keys.</summary>
    [RelayCommand]
    private void ResolveByReassign(Conflict? conflict)
    {
        if (conflict is null)
        {
            return;
        }

        // The first claimant keeps the key. Reassigning all of them would move
        // a bind the user may well have chosen deliberately.
        foreach (var action in conflict.Actions.Skip(1))
        {
            var row = _all.First(r => r.Action.Id == action.Id);
            row.Binding = ConflictDetector.SuggestFree(AllBindings, row.Action.Device);
        }

        Refresh();
    }

    /// <summary>Opens or closes the conflict-resolution panel.</summary>
    [RelayCommand]
    private void ToggleConflictsPanel() => ShowConflictsPanel = !ShowConflictsPanel;

    /// <summary>Closes the conflict-resolution panel.</summary>
    [RelayCommand]
    private void CloseConflictsPanel() => ShowConflictsPanel = false;

    /// <summary>
    /// Suggests a free key for one action and moves it there — the single-action
    /// answer to "get this off the key it is fighting over".
    /// </summary>
    [RelayCommand]
    private void SuggestFree(BindingRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Binding = ConflictDetector.SuggestFree(AllBindings, row.Action.Device);
        Refresh();
    }

    /// <summary>Shows the diff of what Apply will write, before it writes it.</summary>
    [RelayCommand]
    private void PreviewApply()
    {
        if (HasPending)
        {
            ShowApplyPreview = true;
        }
    }

    /// <summary>Closes the apply preview without writing.</summary>
    [RelayCommand]
    private void CancelApplyPreview() => ShowApplyPreview = false;

    /// <summary>
    /// Restores the config files from the backups the last apply left, undoing it
    /// even after the app was closed.
    /// </summary>
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            StatusMessage = "No game folder is set, so there is nothing to restore.";
            return;
        }

        var restored = await _writer.RestoreBackupsAsync(_gamePath, _all.Select(row => row.Action).ToList())
            .ConfigureAwait(true);

        if (restored.Count == 0)
        {
            StatusMessage = "No backups were found — there is no earlier apply to undo.";
            return;
        }

        // Re-read every action off the just-restored files and adopt those values
        // as the new baseline, so the screen shows what is now on disk with nothing
        // left staged.
        var onDisk = await _writer.ReadAsync(_gamePath, _all.Select(row => row.Action).ToList()).ConfigureAwait(true);
        foreach (var row in _all)
        {
            if (onDisk.TryGetValue(row.Action.Id, out var binding))
            {
                row.Binding = binding;
            }

            row.Commit();
        }

        Refresh();
        StatusMessage = $"Restored {restored.Count} config file(s) from the backup taken before the last apply.";
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

    /// <summary>
    /// Writes every binding into the config file that owns it — backing each up
    /// first — then accepts the result as the new baseline.
    /// </summary>
    /// <remarks>
    /// The whole scheme is written, not only the staged rows, so a file that
    /// drifted underneath the app is brought back into line. Only files that
    /// actually differ are touched, and each is copied to a <c>.bak</c> sibling
    /// before it changes.
    /// </remarks>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        ShowApplyPreview = false;

        if (!string.IsNullOrWhiteSpace(_gamePath) && Directory.Exists(_gamePath))
        {
            var result = await _writer.WriteAsync(_gamePath, AllBindings).ConfigureAwait(true);

            // The safeguard: read every bind back out of the game files and confirm it
            // landed, and check the applied scheme is clash-free — so "applied" means
            // the game will really load it, not just that a write was attempted.
            var checks = await _writer.VerifyAsync(_gamePath, AllBindings).ConfigureAwait(true);
            StatusMessage = Describe(result, checks, Conflicts.Count);
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

    private static string Describe(ControlWriteResult result, IReadOnlyList<BindingCheck> checks, int conflicts)
    {
        var verified = checks.Count(c => c.Result == BindCheckResult.Verified);
        var failed = checks.Where(c => c.Failed).ToList();

        // The write result first (what moved, backups, missing files), then the
        // read-back verdict — the part that proves it actually took.
        var written = Describe(result);

        if (failed.Count > 0)
        {
            var names = string.Join(", ", failed.Take(3).Select(c => c.ActionName));
            return $"{written} Checked the game files: {failed.Count} bind(s) did not take ({names}). "
                + "Try Apply again, or check that mod is installed.";
        }

        var clash = conflicts == 0
            ? "no clashes"
            : $"{conflicts} clash(es) still to resolve";

        return $"{written} Confirmed {verified} bind(s) in the game files — {clash}.";
    }

    /// <summary>
    /// Reads the bindings currently on disk back in, staging anything a mod's
    /// in-game menu changed so it can be reviewed before it overwrites the profile.
    /// </summary>
    /// <remarks>
    /// Several mods write straight to their own ini from an in-game menu, so after
    /// a session the stored profile and the real files drift. Without this the app
    /// would confidently overwrite a setting the user changed in-game, which is
    /// worse than not having the app.
    /// </remarks>
    [RelayCommand]
    private async Task ReadFromGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            StatusMessage = "No game folder is set, so there is nothing to read.";
            return;
        }

        // Read back every action on screen — curated and discovered — so an
        // in-game change to any of them is reconciled, not just the catalogued ones.
        var onDisk = await _writer.ReadAsync(_gamePath, _all.Select(row => row.Action).ToList()).ConfigureAwait(true);

        var changed = 0;
        foreach (var row in _all)
        {
            if (onDisk.TryGetValue(row.Action.Id, out var binding) && binding != row.Binding)
            {
                row.Binding = binding;
                changed++;
            }
        }

        // A mod added since the last look brings new binds; fold them in too.
        var discovered = await ScanKeybindsAsync().ConfigureAwait(true);

        StatusMessage = (changed, discovered) switch
        {
            (0, 0) => "The config files match your profile — nothing changed in-game.",
            (_, 0) => $"{changed} bind(s) were changed in-game. Review the staged changes, then apply to keep them.",
            (0, _) => $"Found {discovered} new bind(s) from a mod that was not here before.",
            _ => $"{changed} bind(s) changed in-game and {discovered} new bind(s) were found. Review, then apply.",
        };

        Refresh();
    }

    /// <summary>
    /// Loads once, on first appearance: reads the real bindings off disk as the
    /// baseline and folds in every keybind a scan finds beyond the catalogue.
    /// </summary>
    /// <remarks>
    /// This is what makes the screen show what the game will actually read, and what
    /// lets an uncatalogued mod's keys — keyboard on the keyboard tab, controller on
    /// the controller tab — appear without anyone having to add them by hand.
    /// </remarks>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadFromDiskAsync().ConfigureAwait(true);
    }

    private async Task LoadFromDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            return;
        }

        // Curated binds: adopt the on-disk value as the baseline, so the screen opens
        // showing reality rather than the suggested scheme with nothing to apply.
        var onDisk = await _writer.ReadAsync(_gamePath, ControlCatalogue.Actions).ConfigureAwait(true);
        foreach (var row in _all)
        {
            if (onDisk.TryGetValue(row.Action.Id, out var binding))
            {
                row.Binding = binding;
                row.Commit();
            }
        }

        await ScanKeybindsAsync().ConfigureAwait(true);
        Refresh();
    }

    /// <summary>
    /// Adds every keybind a scan found that the catalogue does not already own, bound
    /// to its on-disk value. Already-seen binds are skipped. Returns how many were new.
    /// </summary>
    private async Task<int> ScanKeybindsAsync()
    {
        if (string.IsNullOrWhiteSpace(_gamePath) || !Directory.Exists(_gamePath))
        {
            return 0;
        }

        var scan = await _scanner.ScanAllAsync(_gamePath).ConfigureAwait(true);

        // Anything the curated catalogue already presents in a nicer form is left to it.
        var curated = ControlCatalogue.Actions
            .Select(action => $"{action.ConfigFile}|{action.ConfigKey}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var found in scan.Keybinds)
        {
            var key = $"{found.Action.ConfigFile}|{found.Action.ConfigKey}";
            if (curated.Contains(key) || !_discoveredKeybindKeys.Add(key))
            {
                continue;
            }

            // The row's baseline is the on-disk value, so a discovered bind opens
            // unchanged and only stages once the user edits it.
            _all.Add(new BindingRow(found.Action, found.Binding));
            added++;
        }

        return added;
    }

    /// <summary>The current scheme as a printable Markdown cheat sheet.</summary>
    public string BuildCheatSheet() => CheatSheet.BuildMarkdown(AllBindings, _officer);

    /// <summary>Saves the cheat sheet to the user's Desktop.</summary>
    [RelayCommand]
    private async Task ExportCheatSheetAsync()
    {
        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
        var path = Path.Combine(desktop, "Dispatch Cheat Sheet.md");

        try
        {
            await File.WriteAllTextAsync(path, BuildCheatSheet()).ConfigureAwait(true);
            StatusMessage = $"Cheat sheet saved to {path}";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Could not save the cheat sheet: {ex.Message}";
        }
    }

    private static string Describe(ControlWriteResult result)
    {
        if (result.IsEmpty && result.MissingFiles.Count == 0)
        {
            return "Nothing to write — the config files already match.";
        }

        var written = result.IsEmpty
            ? "No values changed"
            : $"Wrote {result.Changes.Count} value(s) across {result.FilesTouched} file(s); each was backed up first";

        if (result.MissingFiles.Count == 0)
        {
            return written + ".";
        }

        return written +
            $". {result.MissingFiles.Count} file(s) do not exist yet and were skipped — some mods write their config on first launch.";
    }

    /// <summary>Loads a named scheme, staging the differences rather than writing.</summary>
    public void LoadScheme(string name, IReadOnlyDictionary<string, KeyBinding> scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        foreach (var row in _all)
        {
            row.Binding = scheme.TryGetValue(row.Action.Id, out var binding) ? binding : KeyBinding.Unbound;
        }

        ProfileName = name;
        Refresh();
    }

    partial void OnProfileNameChanged(string value)
    {
        // Switching profiles stages the differences rather than writing them,
        // so the diff can be reviewed like any other edit.
        var scheme = value switch
        {
            "Default" => ControlCatalogue.Factory,
            "Suggested" => ControlCatalogue.Suggested,
            _ => (IReadOnlyDictionary<string, KeyBinding>?)null,
        };

        if (scheme is null)
        {
            return;
        }

        foreach (var row in _all)
        {
            row.Binding = scheme.TryGetValue(row.Action.Id, out var binding) ? binding : KeyBinding.Unbound;
        }

        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnDeviceChanged(InputDevice value)
    {
        // A key selected on the keyboard map means nothing on the controller tab.
        SelectedKey = null;
        OnPropertyChanged(nameof(ShowKeyboardMap));
        Refresh();
    }

    partial void OnLayerChanged(KeyModifier value)
    {
        OnPropertyChanged(nameof(LayerLabel));
        Refresh();
    }

    partial void OnPluginFilterChanged(string value) => Refresh();

    partial void OnConflictsOnlyChanged(bool value) => Refresh();

    partial void OnKeyPanelSearchChanged(string value) =>
        OnPropertyChanged(nameof(AssignableActions));

    partial void OnSelectedKeyChanged(KeyToken? value)
    {
        // A fresh key opens the panel on an empty search.
        KeyPanelSearch = string.Empty;
        Refresh();
    }

    /// <summary>Recomputes the visible rows and every derived count.</summary>
    private void Refresh()
    {
        var conflicts = Conflicts;
        var conflicted = conflicts
            .SelectMany(conflict => conflict.Actions.Select(action => action.Id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in _all)
        {
            row.IsConflicted = conflicted.Contains(row.Action.Id);
        }

        // Nothing to resolve means nothing to show; close the panel so it never
        // lingers empty after the last conflict is cleared.
        if (conflicts.Count == 0)
        {
            ShowConflictsPanel = false;
        }

        // Keyboard and controller are separate namespaces; the tab picks one.
        var visible = _all.Where(row => row.Action.Device == Device);

        if (PluginFilter != AllPlugins)
        {
            visible = visible.Where(row => row.Action.Plugin == PluginFilter);
        }

        if (ConflictsOnly)
        {
            visible = visible.Where(row => row.IsConflicted);
        }

        // Clicking a key opens the mapping panel, which owns the "what is on this
        // key?" view; the main list stays whole so the panel and the list answer
        // two different questions at once.
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();

            // Matching the displayed key as well as the name is what lets
            // someone type F9 and find out what is on it.
            visible = visible.Where(row =>
                row.Action.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Action.Plugin.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Action.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Action.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.BindingDisplay.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Rows.Clear();
        foreach (var row in visible.OrderBy(r => r.Action.Plugin, StringComparer.Ordinal)
                                   .ThenBy(r => r.Action.Name, StringComparer.Ordinal))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(AllBindings));
        OnPropertyChanged(nameof(PluginFilters));
        OnPropertyChanged(nameof(Conflicts));
        OnPropertyChanged(nameof(ConflictSummary));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingDiff));
        OnPropertyChanged(nameof(IsKeyPanelOpen));
        OnPropertyChanged(nameof(SelectedKeyDisplay));
        OnPropertyChanged(nameof(ActionsOnSelectedKey));
        OnPropertyChanged(nameof(SelectedKeyHasActions));
        OnPropertyChanged(nameof(AssignableActions));
    }
}
