namespace Dispatch.Core.Installation;

/// <summary>What to install, and where.</summary>
/// <param name="GamePath">Root of the validated GTA V installation.</param>
/// <param name="PresetName">Which setup was chosen.</param>
/// <param name="ModCount">How many mods the preset contains.</param>
public sealed record InstallRequest(string GamePath, string PresetName, int ModCount);

/// <summary>
/// Executes an install run, reporting progress as it goes.
/// </summary>
/// <remarks>
/// The interface exists now so the install screen is written against the real
/// shape from the start. <see cref="SimulatedInstallRunner"/> satisfies it
/// until the file-writing implementation lands, at which point swapping them
/// is a container registration rather than a UI change.
/// </remarks>
public interface IInstallRunner
{
    /// <summary>
    /// Runs the install. Honours cancellation between atomic operations, never
    /// mid-file-write.
    /// </summary>
    /// <param name="request">What to install.</param>
    /// <param name="progress">Receives a snapshot as each step completes.</param>
    /// <param name="cancellationToken">Stops the run after the current operation.</param>
    /// <returns>A report describing what happened, whether or not it succeeded.</returns>
    Task<InstallReport> RunAsync(
        InstallRequest request,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default);
}
