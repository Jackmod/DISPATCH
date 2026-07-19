namespace Dispatch.Core.Resilience;

/// <summary>The environment facts preflight needs, behind an interface so Core stays portable.</summary>
public interface IEnvironmentProbe
{
    /// <summary>Names of processes that would lock game files, if any are running.</summary>
    IReadOnlyList<string> RunningGameProcesses();

    /// <summary>Whether the given folder can be written to without elevation.</summary>
    bool CanWriteTo(string path);

    /// <summary>Free bytes on the drive containing the given path, or null if unknown.</summary>
    long? FreeBytesOn(string path);

    /// <summary>Whether a known host resolves, standing in for "is there internet".</summary>
    bool IsNetworkReachable();

    /// <summary>Whether another instance of Dispatch already holds the single-instance lock.</summary>
    bool AnotherInstanceRunning();

    /// <summary>Whether the path sits inside a sync client's folder (OneDrive, Dropbox).</summary>
    bool IsInsideSyncFolder(string path);
}

/// <summary>A single preflight finding.</summary>
/// <param name="Passed">Whether this check passed.</param>
/// <param name="Code">Error catalogue code when it failed, for the remedy.</param>
/// <param name="Summary">One line, shown whether it passed or not.</param>
/// <param name="IsBlocking">Whether a failure stops the run, or is only a warning.</param>
public sealed record PreflightFinding(bool Passed, string? Code, string Summary, bool IsBlocking);

/// <summary>The outcome of running every check.</summary>
/// <param name="Findings">Every check, in the order they ran.</param>
public sealed record PreflightResult(IReadOnlyList<PreflightFinding> Findings)
{
    /// <summary>True when nothing blocking failed — the run may start.</summary>
    public bool CanProceed => Findings.All(f => f.Passed || !f.IsBlocking);

    /// <summary>The blocking failures, if any.</summary>
    public IReadOnlyList<PreflightFinding> Blockers =>
        Findings.Where(f => !f.Passed && f.IsBlocking).ToList();

    /// <summary>The non-blocking warnings.</summary>
    public IReadOnlyList<PreflightFinding> Warnings =>
        Findings.Where(f => !f.Passed && !f.IsBlocking).ToList();
}

/// <summary>
/// Everything checked before a byte is written, refusing clearly on any failure.
/// </summary>
/// <remarks>
/// The whole point is to fail before starting a run that cannot finish, because
/// a locked file or a full disk discovered halfway through is a miserable,
/// half-modded failure. Each check maps to a specific error-catalogue code, so a
/// failure comes with a remedy rather than a shrug.
///
/// <para>
/// A running game is the check that matters most — it is both the most common
/// cause of a mid-install failure and the easiest to catch up front.
/// </para>
/// </remarks>
public sealed class PreflightCheck
{
    private readonly IEnvironmentProbe _env;

    /// <summary>Constructs the check over an environment probe.</summary>
    public PreflightCheck(IEnvironmentProbe env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    /// <summary>Runs every check against a target game folder and required size.</summary>
    /// <param name="gamePath">The install destination.</param>
    /// <param name="requiredBytes">Download plus placement size; headroom is added on top.</param>
    public PreflightResult Run(string gamePath, long requiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var findings = new List<PreflightFinding>
        {
            CheckGameNotRunning(),
            CheckNoOtherInstance(),
            CheckNetwork(),
            CheckWritePermission(gamePath),
            CheckDiskSpace(gamePath, requiredBytes),
            CheckSyncFolder(gamePath),
        };

        return new PreflightResult(findings);
    }

    private PreflightFinding CheckGameNotRunning()
    {
        var running = _env.RunningGameProcesses();
        return running.Count == 0
            ? Pass("GTA V is not running.")
            : Block("GAME_RUNNING", $"Close these first: {string.Join(", ", running)}.");
    }

    private PreflightFinding CheckNoOtherInstance() =>
        _env.AnotherInstanceRunning()
            ? Block("OTHER_INSTANCE", "Another copy of Dispatch is already running.")
            : Pass("No other instance of Dispatch is running.");

    private PreflightFinding CheckNetwork() =>
        _env.IsNetworkReachable()
            ? Pass("Internet connection is available.")
            : Block("NO_NETWORK", "No internet connection; the mods cannot be fetched.");

    private PreflightFinding CheckWritePermission(string gamePath) =>
        _env.CanWriteTo(gamePath)
            ? Pass("The game folder can be written to.")
            : Block("NEEDS_ELEVATION", "The game folder needs administrator access to change.");

    private PreflightFinding CheckDiskSpace(string gamePath, long requiredBytes)
    {
        // 50% headroom for staging, as the spec requires.
        var needed = (long)(requiredBytes * 1.5);
        var free = _env.FreeBytesOn(gamePath);

        if (free is null)
        {
            return Warn("DISK_UNKNOWN", "Could not read free space on the game drive.");
        }

        return free >= needed
            ? Pass($"Enough disk space ({Gb(free.Value)} free).")
            : Block("DISK_FULL", $"Needs {Gb(needed)} free, has {Gb(free.Value)}.");
    }

    private PreflightFinding CheckSyncFolder(string gamePath) =>
        _env.IsInsideSyncFolder(gamePath)
            ? Warn("SYNC_FOLDER", "The game folder is inside OneDrive or Dropbox. Pause syncing before installing.")
            : Pass("The game folder is not inside a sync client.");

    private static PreflightFinding Pass(string summary) => new(true, null, summary, IsBlocking: true);

    private static PreflightFinding Block(string code, string summary) => new(false, code, summary, IsBlocking: true);

    private static PreflightFinding Warn(string code, string summary) => new(false, code, summary, IsBlocking: false);

    private static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";
}
