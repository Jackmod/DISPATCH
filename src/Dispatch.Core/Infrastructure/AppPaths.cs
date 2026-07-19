namespace Dispatch.Core.Infrastructure;

/// <summary>
/// Every location Dispatch writes to, resolved once and injected everywhere.
/// Nothing in the codebase composes one of these paths by hand.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root of all Dispatch state. <c>%LOCALAPPDATA%/Dispatch</c> on Windows.</summary>
    string Root { get; }

    /// <summary>Officer identity, control profiles, tuning and appearance.</summary>
    string ProfileFile { get; }

    /// <summary>What was installed, when, against which game build, with hashes.</summary>
    string InstallRecordFile { get; }

    /// <summary>Run journals, one <c>.jsonl</c> per install run.</summary>
    string RunsDirectory { get; }

    /// <summary>Per-run backups of files overwritten during placement.</summary>
    string BackupsDirectory { get; }

    /// <summary>Where the folder cleaner moves things instead of deleting them.</summary>
    string QuarantineDirectory { get; }

    /// <summary>Downloaded mod archives, kept so a repair needs no network.</summary>
    string ArchivesDirectory { get; }

    /// <summary>Rolling Serilog output.</summary>
    string LogsDirectory { get; }

    /// <summary>Scratch space for extraction. Under <c>%TEMP%</c>, not under <see cref="Root"/>.</summary>
    string StagingRoot { get; }

    /// <summary>Creates any of the above that do not yet exist.</summary>
    void EnsureCreated();
}

/// <inheritdoc />
public sealed class AppPaths : IAppPaths
{
    private const string AppFolderName = "Dispatch";

    /// <summary>Uses the current user's local application data and temp directories.</summary>
    public AppPaths()
        : this(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify),
                AppFolderName),
            Path.Combine(Path.GetTempPath(), AppFolderName))
    {
    }

    /// <summary>Roots both trees explicitly. Tests use this to stay inside a temp directory.</summary>
    public AppPaths(string root, string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);

        Root = root;
        StagingRoot = Path.Combine(tempRoot, "staging");
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public string StagingRoot { get; }

    /// <inheritdoc />
    public string ProfileFile => Path.Combine(Root, "profile.json");

    /// <inheritdoc />
    public string InstallRecordFile => Path.Combine(Root, "install-record.json");

    /// <inheritdoc />
    public string RunsDirectory => Path.Combine(Root, "runs");

    /// <inheritdoc />
    public string BackupsDirectory => Path.Combine(Root, "backups");

    /// <inheritdoc />
    public string QuarantineDirectory => Path.Combine(Root, "quarantine");

    /// <inheritdoc />
    public string ArchivesDirectory => Path.Combine(Root, "archives");

    /// <inheritdoc />
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <inheritdoc />
    public void EnsureCreated()
    {
        foreach (var directory in new[]
                 {
                     Root,
                     RunsDirectory,
                     BackupsDirectory,
                     QuarantineDirectory,
                     ArchivesDirectory,
                     LogsDirectory,
                     StagingRoot,
                 })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
