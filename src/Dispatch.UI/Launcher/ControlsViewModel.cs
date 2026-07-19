using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Controls;

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

    /// <summary>Constructs the screen against the shipped Suggested scheme.</summary>
    public ControlsViewModel()
        : this(ControlCatalogue.Suggested)
    {
    }

    /// <summary>Constructs the screen against a specific scheme.</summary>
    public ControlsViewModel(IReadOnlyDictionary<string, KeyBinding> scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        foreach (var bound in ControlCatalogue.Bind(scheme))
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

    /// <summary>Every binding, for the keyboard map.</summary>
    public IReadOnlyList<BoundAction> AllBindings =>
        _all.Select(row => new BoundAction(row.Action, row.Binding)).ToList();

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

    /// <summary>Starts capture on a row. The next key pressed becomes its binding.</summary>
    [RelayCommand]
    private void BeginCapture(BindingRow? row) => Capturing = row;

    /// <summary>Cancels capture without changing anything.</summary>
    [RelayCommand]
    private void CancelCapture() => Capturing = null;

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

    /// <summary>Accepts every staged change as the new baseline.</summary>
    [RelayCommand]
    private void Apply()
    {
        // Writing to config files lands with IniDocument. Until then this
        // commits in memory so the staging behaviour is exercisable.
        foreach (var row in _all)
        {
            row.Commit();
        }

        Refresh();
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

    partial void OnLayerChanged(KeyModifier value) => Refresh();

    partial void OnConflictsOnlyChanged(bool value) => Refresh();

    partial void OnSelectedKeyChanged(KeyToken? value) => Refresh();

    /// <summary>Recomputes the visible rows and every derived count.</summary>
    private void Refresh()
    {
        var conflicted = Conflicts
            .SelectMany(conflict => conflict.Actions.Select(action => action.Id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in _all)
        {
            row.IsConflicted = conflicted.Contains(row.Action.Id);
        }

        var visible = _all.AsEnumerable();

        if (ConflictsOnly)
        {
            visible = visible.Where(row => row.IsConflicted);
        }

        // Clicking a key on the map filters the list to it, which is how the
        // map answers "what is on this key?" without a separate panel.
        if (SelectedKey is { } key)
        {
            visible = visible.Where(row => row.Binding.Key == key);
        }

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
        OnPropertyChanged(nameof(Conflicts));
        OnPropertyChanged(nameof(ConflictSummary));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingDiff));
    }
}
