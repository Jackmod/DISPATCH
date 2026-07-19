namespace Dispatch.Core.Installation;

/// <summary>The seven phases of a run, in order.</summary>
public enum InstallPhase
{
    /// <summary>Fetching every archive from its author's server.</summary>
    Collecting,

    /// <summary>Reading versions out of the archives before anything is written.</summary>
    CheckingCompatibility,

    /// <summary>Backing up anything about to be overwritten.</summary>
    BackingUp,

    /// <summary>Moving validated files from staging into the game folder.</summary>
    PlacingFiles,

    /// <summary>Writing config values in place.</summary>
    WritingConfiguration,

    /// <summary>Texture installs via OpenIV.</summary>
    InstallingTextures,

    /// <summary>Hashing what was placed and checking it survived.</summary>
    Verifying,
}

/// <summary>
/// A snapshot of a run, reported as it progresses.
/// </summary>
/// <param name="Phase">Which phase is running.</param>
/// <param name="Detail">What is happening right now, for the mono line.</param>
/// <param name="Completed">Items finished.</param>
/// <param name="Total">Items in the run.</param>
public readonly record struct InstallProgress(
    InstallPhase Phase,
    string Detail,
    int Completed,
    int Total)
{
    /// <summary>Human-readable phase name, shown large on the install screen.</summary>
    public string PhaseName => Phase switch
    {
        InstallPhase.Collecting => "Collecting files",
        InstallPhase.CheckingCompatibility => "Checking compatibility",
        InstallPhase.BackingUp => "Backing up",
        InstallPhase.PlacingFiles => "Placing files",
        InstallPhase.WritingConfiguration => "Writing configuration",
        InstallPhase.InstallingTextures => "Installing textures",
        InstallPhase.Verifying => "Verifying",
        _ => "Working",
    };

    /// <summary>Zero-based phase index, for the progress rail.</summary>
    public int PhaseIndex => (int)Phase;
}

/// <summary>
/// What a finished run produced. Every run ends with one of these, whether it
/// succeeded or not.
/// </summary>
/// <param name="Installed">Mods that installed cleanly.</param>
/// <param name="NeedsAttention">Mods that failed, each with a specific reason.</param>
/// <param name="Skipped">Mods deliberately not installed, each with a reason.</param>
/// <param name="Elapsed">Wall-clock duration of the run.</param>
public sealed record InstallReport(
    IReadOnlyList<string> Installed,
    IReadOnlyList<InstallProblem> NeedsAttention,
    IReadOnlyList<InstallProblem> Skipped,
    TimeSpan Elapsed)
{
    /// <summary>True when nothing needs attention.</summary>
    public bool IsClean => NeedsAttention.Count == 0;
}

/// <summary>One thing that did not install, and why.</summary>
/// <param name="Mod">Display name of the mod.</param>
/// <param name="Reason">Plain-language reason, written for someone who was not there.</param>
public sealed record InstallProblem(string Mod, string Reason);
