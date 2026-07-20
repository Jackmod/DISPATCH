using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Detection;

/// <summary>Where a setup stands relative to the game build it was installed against.</summary>
public enum GameBuildState
{
    /// <summary>Nothing has been installed yet.</summary>
    NotInstalled,

    /// <summary>The build could not be read on one side, so no comparison is possible.</summary>
    Unknown,

    /// <summary>The game is on the same build the setup was installed against.</summary>
    UpToDate,

    /// <summary>Rockstar has updated the game since the install. Script Hook V no longer matches.</summary>
    GameUpdated,
}

/// <summary>The result of comparing the current game build against the installed-against build.</summary>
/// <param name="State">Which situation holds.</param>
/// <param name="InstalledAgainst">The build the setup was installed against.</param>
/// <param name="CurrentBuild">The build the game is on now.</param>
public sealed record ScriptHookStatus(GameBuildState State, string? InstalledAgainst, string? CurrentBuild)
{
    /// <summary>True when the game moved on and the script hooks need refreshing.</summary>
    public bool NeedsUpdate => State == GameBuildState.GameUpdated;

    /// <summary>A short headline for the launcher.</summary>
    public string Headline => State switch
    {
        GameBuildState.GameUpdated => "Rockstar updated GTA V",
        GameBuildState.UpToDate => "Script Hook V matches your game",
        GameBuildState.NotInstalled => "Nothing installed yet",
        _ => "Game build unconfirmed",
    };

    /// <summary>A plain-language explanation, written for someone who just found LSPDFR broken.</summary>
    public string Detail => State switch
    {
        GameBuildState.GameUpdated =>
            $"The game is now build {CurrentBuild}; your setup was installed against {InstalledAgainst}. "
            + "Script Hook V is locked to the exact build, so it no longer matches — this is why LSPDFR "
            + "stopped loading. Update the script hooks to get back on duty.",
        GameBuildState.UpToDate =>
            $"Still on build {CurrentBuild}, the build you installed against. Everything should load.",
        GameBuildState.NotInstalled =>
            "Run the installer first, then Dispatch will watch for game updates that break Script Hook V.",
        _ => "Dispatch could not read the game build to compare it against your install.",
    };
}

/// <summary>Watches whether a game update has outdated the script hooks.</summary>
public interface IGameBuildWatch
{
    /// <summary>Compares the current game build against the build the setup was installed against.</summary>
    Task<ScriptHookStatus> CheckAsync(string gamePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Compares the game build now against the build recorded at install time.
/// </summary>
/// <remarks>
/// This is the single most common way a working LSPDFR setup dies: Rockstar
/// patches GTA V, the game build moves, and Script Hook V — locked to the exact
/// build — silently stops loading, taking everything downstream with it. The user
/// rarely connects the two. Reading the recorded build and the live build and
/// comparing them turns that invisible failure into a plain "the game updated,
/// update your script hooks" the moment they open Dispatch.
/// </remarks>
public sealed class GameBuildWatch : IGameBuildWatch
{
    private readonly IInstallRecordStore _records;
    private readonly IVersionReader _versions;
    private readonly ILogger<GameBuildWatch> _logger;

    /// <summary>Constructs the watch.</summary>
    public GameBuildWatch(IInstallRecordStore records, IVersionReader versions, ILogger<GameBuildWatch> logger)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(logger);

        _records = records;
        _versions = versions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScriptHookStatus> CheckAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        var record = await _records.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsInstalled)
        {
            return new ScriptHookStatus(GameBuildState.NotInstalled, null, null);
        }

        var installedAgainst = record.GameBuild;
        var currentBuild = string.IsNullOrWhiteSpace(gamePath) ? null : _versions.Read(gamePath).GameBuild;

        if (string.IsNullOrWhiteSpace(installedAgainst) || string.IsNullOrWhiteSpace(currentBuild))
        {
            return new ScriptHookStatus(GameBuildState.Unknown, installedAgainst, currentBuild);
        }

        var state = string.Equals(installedAgainst, currentBuild, StringComparison.Ordinal)
            ? GameBuildState.UpToDate
            : GameBuildState.GameUpdated;

        if (state == GameBuildState.GameUpdated)
        {
            _logger.LogInformation(
                "Game build changed since install: was {Old}, now {New}", installedAgainst, currentBuild);
        }

        return new ScriptHookStatus(state, installedAgainst, currentBuild);
    }
}
