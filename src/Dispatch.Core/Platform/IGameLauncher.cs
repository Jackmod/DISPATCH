namespace Dispatch.Core.Platform;

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
    /// Starts RagePluginHook from the game folder. Returns false when its
    /// executable is not there or could not be started.
    /// </summary>
    bool LaunchRagePluginHook(string gamePath);
}

/// <summary>Used where launching is not possible. Reports unavailable.</summary>
public sealed class NoGameLauncher : IGameLauncher
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public bool LaunchRagePluginHook(string gamePath) => false;
}
