namespace Dispatch.Core.Platform;

/// <summary>What happened when Dispatch tried to start RagePluginHook.</summary>
public enum LaunchOutcome
{
    /// <summary>RagePluginHook was started.</summary>
    Launched,

    /// <summary>Launching is not possible here — not Windows, or no game folder.</summary>
    Unavailable,

    /// <summary>RagePluginHook's executable was not found in the game folder.</summary>
    LoaderNotFound,

    /// <summary>The loader was found but could not be started.</summary>
    Failed,
}

/// <summary>Launches the game the way LSPDFR needs it started.</summary>
/// <remarks>
/// Going on duty means starting RagePluginHook, not GTA V directly — it is the
/// loader that hooks the game and brings the plugins up. Its executable sits in
/// the game folder once LSPDFR is installed, so the folder is all this needs.
/// </remarks>
public interface IGameLauncher
{
    /// <summary>Whether the launcher can start anything on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Starts RagePluginHook from the game folder, reporting exactly what happened
    /// so the launcher can tell the user rather than fail silently.
    /// </summary>
    LaunchOutcome LaunchRagePluginHook(string gamePath);
}

/// <summary>Used where launching is not possible. Reports unavailable.</summary>
public sealed class NoGameLauncher : IGameLauncher
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public LaunchOutcome LaunchRagePluginHook(string gamePath) => LaunchOutcome.Unavailable;
}
