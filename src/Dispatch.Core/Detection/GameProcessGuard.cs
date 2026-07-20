using System.Diagnostics;

namespace Dispatch.Core.Detection;

/// <summary>Tells whether the game or its loader is currently running.</summary>
/// <remarks>
/// Installing or cleaning while GTA V or RagePluginHook is open means writing to
/// files the game has locked — the write fails partway and leaves a half-applied
/// mess. Checking first turns that into a plain "close the game and try again".
/// </remarks>
public interface IGameProcessGuard
{
    /// <summary>True when GTA V or RagePluginHook is running right now.</summary>
    bool IsGameRunning(out string? processName);
}

/// <summary>Looks for the game's processes by name.</summary>
public sealed class GameProcessGuard : IGameProcessGuard
{
    // The executables that hold game-folder files open. Names only, no extension,
    // as Process.GetProcessesByName expects.
    private static readonly string[] Names =
        ["GTA5", "GTA5_Enhanced", "PlayGTAV", "RAGEPluginHook", "GTAVLauncher", "Grand Theft Auto V"];

    /// <inheritdoc />
    public bool IsGameRunning(out string? processName)
    {
        foreach (var name in Names)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    processName = name;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // A process exiting mid-enumeration; treat as not found.
            }
        }

        processName = null;
        return false;
    }
}
