namespace Dispatch.Core.Platform;

/// <summary>Manages a Windows Defender exclusion for the game folder.</summary>
/// <remarks>
/// Defender routinely quarantines Script Hook V, the ASI loaders and
/// RagePluginHook as threats — they hook a running process, which is exactly what
/// a cheat does, so the heuristics fire. It is behind a large share of "nothing
/// loads" reports. Adding the game folder as an exclusion, with the user's
/// blessing, stops it before it starts.
/// </remarks>
public interface IDefenderService
{
    /// <summary>Whether adding an exclusion is possible on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether the folder is already excluded. Null when it cannot be determined.</summary>
    Task<bool?> IsExcludedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a Defender exclusion for the folder. Needs elevation; returns false
    /// when it was refused or is unavailable.
    /// </summary>
    Task<bool> AddExclusionAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Used off Windows, or where Defender cannot be reached. Reports unavailable.</summary>
public sealed class NoDefenderService : IDefenderService
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<bool?> IsExcludedAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(null);

    /// <inheritdoc />
    public Task<bool> AddExclusionAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
