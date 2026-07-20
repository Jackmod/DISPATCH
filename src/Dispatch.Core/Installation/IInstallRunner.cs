namespace Dispatch.Core.Installation;

/// <summary>What to install, and where.</summary>
/// <param name="GamePath">Root of the validated GTA V installation.</param>
/// <param name="PresetName">Which setup was chosen, for display.</param>
/// <param name="ModCount">How many mods the preset contains.</param>
/// <param name="PresetId">
/// The catalogue preset id the real runner reads to know which mods to fetch.
/// Empty for the simulated runner, which needs no catalogue.
/// </param>
/// <param name="GameBuild">The detected GTA V build, recorded with the install.</param>
/// <param name="ModIds">
/// The exact mods to install. When set, ONLY these are unpacked and placed —
/// the guarantee that a user gets nothing they did not pick. When null, the
/// whole preset is installed. Ids not in the catalogue are ignored.
/// </param>
/// <param name="Officer">
/// The values that personalise the config files — callsign, name, department,
/// air-unit callsign. Null uses sensible defaults.
/// </param>
public sealed record InstallRequest(
    string GamePath,
    string PresetName,
    int ModCount,
    string PresetId = "",
    string? GameBuild = null,
    IReadOnlyList<string>? ModIds = null,
    Configuration.OfficerValues? Officer = null);

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
