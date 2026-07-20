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

    /// <summary>
    /// User-supplied mod archives for the sources that cannot be fetched
    /// automatically — LSPDFR and the lcpdfr/gta5-mods plugins. Dropped here
    /// once, they install without a network round-trip.
    /// </summary>
    string ModPackDirectory { get; }

    /// <summary>
    /// Files that must be imported through OpenIV rather than copied into the game
    /// folder — textures and RPF content. The installer sets them aside here,
    /// organised by mod, for the user to bring in by hand.
    /// </summary>
    string OpenIvImportDirectory { get; }

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
    private const string DesktopImportFolderName = "Dispatch OpenIV Import";

    private readonly string _openIvImport;

    /// <summary>
    /// Keeps the machinery — logs, backups, quarantine, journals — in the usual
    /// hidden <c>%LOCALAPPDATA%</c>, but puts the one folder the user has to work
    /// with by hand, the OpenIV import set, on the Desktop for quick access.
    /// </summary>
    /// <remarks>
    /// Backups and journals live where they cannot be deleted by accident; the
    /// import folder lives where it cannot be missed. A clearly named Desktop
    /// folder rather than the whole working directory keeps the desktop tidy and
    /// avoids colliding with a folder that happens to share the app's name.
    /// </remarks>
    public AppPaths()
        : this(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify),
                AppFolderName),
            Path.Combine(Path.GetTempPath(), AppFolderName),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolderOption.DoNotVerify),
                DesktopImportFolderName))
    {
    }

    /// <summary>Roots the trees explicitly. Tests use this to stay inside a temp directory.</summary>
    /// <param name="root">The working-data root.</param>
    /// <param name="tempRoot">The temp root for staging.</param>
    /// <param name="openIvImport">
    /// Where OpenIV files are set aside. Null keeps them under <paramref name="root"/>,
    /// which is what tests want; production points this at the Desktop.
    /// </param>
    public AppPaths(string root, string tempRoot, string? openIvImport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);

        Root = root;
        StagingRoot = Path.Combine(tempRoot, "staging");
        _openIvImport = openIvImport ?? Path.Combine(root, "OpenIV Import");
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
    public string ModPackDirectory => Path.Combine(Root, "modpack");

    /// <inheritdoc />
    public string OpenIvImportDirectory => _openIvImport;

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
                     ModPackDirectory,
                     OpenIvImportDirectory,
                     LogsDirectory,
                     StagingRoot,
                 })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
