using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Controls;
using Dispatch.Core.Palette;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The Ctrl+K palette: a live-filtered list of everywhere to go and everything
/// to do, ranked as the user types.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteEntry> _all;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteEntry? _selected;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Constructs the palette over the app's navigable surface.</summary>
    public CommandPaletteViewModel()
    {
        _all = BuildEntries();
        Results = [];
        Refresh();
    }

    /// <summary>The ranked results for the current query.</summary>
    public ObservableCollection<PaletteEntry> Results { get; }

    /// <summary>Raised when an entry is chosen, for the host to act on.</summary>
    public event EventHandler<PaletteEntry>? Chosen;

    /// <summary>Opens the palette, cleared and ready for input.</summary>
    public void Open()
    {
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    /// <summary>Closes the palette without choosing anything.</summary>
    public void Close() => IsOpen = false;

    /// <summary>Moves the highlight down the list, wrapping.</summary>
    public void MoveDown() => Move(1);

    /// <summary>Moves the highlight up the list, wrapping.</summary>
    public void MoveUp() => Move(-1);

    /// <summary>Chooses the highlighted entry.</summary>
    public void ChooseSelected()
    {
        if (Selected is { } entry)
        {
            Choose(entry);
        }
    }

    /// <summary>Chooses a specific entry, closing the palette.</summary>
    public void Choose(PaletteEntry entry)
    {
        IsOpen = false;
        Chosen?.Invoke(this, entry);
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        index = ((index + delta) % Results.Count + Results.Count) % Results.Count;
        Selected = Results[index];
    }

    private void Refresh()
    {
        var matches = CommandPalette.Search(Query, _all);

        Results.Clear();
        foreach (var entry in matches)
        {
            Results.Add(entry);
        }

        // Keep a highlight on the top result so Enter always does something.
        Selected = Results.FirstOrDefault();
    }

    private static IReadOnlyList<PaletteEntry> BuildEntries()
    {
        var entries = new List<PaletteEntry>
        {
            new("Dashboard", "The landing screen", "Go to", PaletteAction.Navigate, "dashboard"),
            new("Mods", "Installed mods", "Go to", PaletteAction.Navigate, "mods"),
            new("Controls", "Keybindings", "Go to", PaletteAction.Navigate, "controls"),
            new("Settings", "Appearance and behaviour", "Go to", PaletteAction.Navigate, "settings"),

            new("Clean GTA folder", "Remove stale mod files safely", "Command", PaletteAction.Run, "clean"),
            new("Run audit", "Check the install for problems", "Command", PaletteAction.Run, "audit"),
            new("Go on duty", "Launch RagePluginHook", "Command", PaletteAction.Run, "onduty"),
        };

        // Every keybindable action is reachable by name, so "arrest" jumps
        // straight to that bind rather than making the user find the screen
        // and then the row.
        entries.AddRange(ControlCatalogue.Actions.Select(action =>
            new PaletteEntry(
                action.Name,
                $"{action.Plugin} keybinding",
                "Keybind",
                PaletteAction.EditBinding,
                action.Id)));

        return entries;
    }
}
