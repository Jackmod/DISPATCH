using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Controls;
using Dispatch.Core.Profiles;
using Dispatch.UI.Controls;
using Dispatch.UI.Imagery;

namespace Dispatch.UI.Launcher;

/// <summary>One tile in the dashboard status grid.</summary>
/// <param name="Eyebrow">Small uppercase label.</param>
/// <param name="Value">The headline value.</param>
/// <param name="Detail">A line of context beneath it.</param>
/// <param name="Tone">Status colour.</param>
public sealed record StatusTile(string Eyebrow, string Value, string Detail, StatusTone Tone);

/// <summary>
/// The dashboard: officer hero, a go-on-duty button, and a grid of status
/// tiles.
/// </summary>
/// <remarks>
/// The tiles read from the install record and the game folder in the finished
/// app. Until detection and the installer land they carry representative
/// values, so the layout and the wording are settled before there is live data
/// to hang on them.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>Constructs the dashboard for an officer.</summary>
    public DashboardViewModel(OfficerProfile? officer = null)
    {
        Officer = officer;

        var conflicts = ConflictDetector.Detect(ControlCatalogue.Bind(ControlCatalogue.Suggested)).Count;

        Tiles =
        [
            new StatusTile("GAME BUILD", "1.0.3725", "Matches your install", StatusTone.Good),
            new StatusTile("SCRIPT HOOK V", "1.0.3521.0", "Compatible", StatusTone.Good),
            new StatusTile("RAGEPLUGINHOOK", "1.114", "Loaded", StatusTone.Good),
            new StatusTile("LSPDFR", "0.4.9", "Loaded", StatusTone.Good),
            new StatusTile("PLUGINS", "40 installed", "None errored last session", StatusTone.Neutral),
            new StatusTile(
                "KEYBINDS",
                conflicts == 0 ? "No conflicts" : $"{conflicts} conflicts",
                "Suggested scheme",
                conflicts == 0 ? StatusTone.Good : StatusTone.Bad),
            new StatusTile("LAST SESSION", "1h 12m", "6 callouts · 4 arrests · 2 pursuits", StatusTone.Neutral),
            new StatusTile("HEALTH", "All good", "Last audit passed", StatusTone.Good),
        ];
    }

    /// <summary>The officer on duty.</summary>
    public OfficerProfile? Officer { get; }

    /// <summary>Officer name, or a placeholder.</summary>
    public string OfficerName => Officer?.Name ?? "Officer";

    /// <summary>Callsign, or a placeholder.</summary>
    public string Callsign => Officer?.Callsign ?? "1 ADAM 7";

    /// <summary>Agency code.</summary>
    public string Agency => Officer?.AgencyCode ?? "LSPD";

    /// <summary>The status tiles.</summary>
    public IReadOnlyList<StatusTile> Tiles { get; }

    /// <summary>A backdrop photograph, if any are compiled in.</summary>
    public Avalonia.Media.IImage? Hero { get; } = ImageCatalog.For("dashboard", 0);

    /// <summary>True when there is a hero photograph.</summary>
    public bool HasHero => Hero is not null;
}

/// <summary>Placeholder for the Mods section until it is built.</summary>
public sealed class ModsViewModel;

/// <summary>Placeholder for the Settings section until it is built.</summary>
public sealed class SettingsViewModel;
