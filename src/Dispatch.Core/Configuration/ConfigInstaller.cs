using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Configuration;

/// <summary>What configuring one mod did.</summary>
/// <param name="ModId">The mod.</param>
/// <param name="File">The file edited, relative to the game folder, or null when none was found.</param>
/// <param name="Applied">Settings that were written.</param>
/// <param name="Unmatched">Settings whose key was not present in the file.</param>
public sealed record ModConfigOutcome(
    string ModId,
    string? File,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Unmatched);

/// <summary>The result of the whole configuration pass.</summary>
/// <param name="Outcomes">One entry per mod that had edits to apply.</param>
public sealed record ConfigInstallReport(IReadOnlyList<ModConfigOutcome> Outcomes)
{
    /// <summary>Mods whose config file could not be found.</summary>
    public IReadOnlyList<string> FilesNotFound =>
        Outcomes.Where(o => o.File is null).Select(o => o.ModId).ToList();

    /// <summary>Total settings successfully written.</summary>
    public int TotalApplied => Outcomes.Sum(o => o.Applied.Count);
}

/// <summary>
/// Applies the config catalogue to a modded game folder: finds each mod's config
/// file and writes the guide's values into it.
/// </summary>
/// <remarks>
/// Runs after placement, over the files that are now in the game folder. It never
/// creates a config file — if the expected one is not there, that mod is reported
/// and skipped, because writing a guessed file would be worse than leaving the
/// mod on its defaults. Editing is in place through <see cref="IniConfigWriter"/>,
/// so a hand-tuned file keeps everything the guide did not touch.
/// </remarks>
public sealed class ConfigInstaller
{
    private readonly IniConfigWriter _writer;
    private readonly ILogger<ConfigInstaller> _logger;

    /// <summary>Constructs the installer.</summary>
    public ConfigInstaller(IniConfigWriter writer, ILogger<ConfigInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logger);

        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// Configures every given mod that has catalogue edits, in catalogue order.
    /// </summary>
    /// <param name="gamePath">The game folder.</param>
    /// <param name="installedModIds">The mods that were installed.</param>
    /// <param name="officer">Values to personalise callsign, name and so on.</param>
    /// <param name="cancellationToken">Stops between files.</param>
    public async Task<ConfigInstallReport> ApplyAsync(
        string gamePath,
        IEnumerable<string> installedModIds,
        OfficerValues officer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(installedModIds);
        ArgumentNullException.ThrowIfNull(officer);

        var installed = installedModIds.ToHashSet(StringComparer.Ordinal);
        var outcomes = new List<ModConfigOutcome>();

        foreach (var config in ConfigCatalogue.All.Where(c => installed.Contains(c.ModId)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = LocateFile(gamePath, config.FileHints);
            if (file is null)
            {
                _logger.LogInformation("No config file found for {Mod} (looked for {Hints})",
                    config.ModId, string.Join(", ", config.FileHints));
                outcomes.Add(new ModConfigOutcome(config.ModId, null, [], config.Settings.Select(s => s.Name).ToList()));
                continue;
            }

            try
            {
                var document = await IniDocument.LoadAsync(file, cancellationToken).ConfigureAwait(false);
                var result = _writer.Apply(document, config.Settings, officer);

                if (result.Changed)
                {
                    await document.SaveAsync(file, cancellationToken).ConfigureAwait(false);
                }

                var relative = Path.GetRelativePath(gamePath, file).Replace('\\', '/');
                var applied = result.Outcomes.Where(o => o.Applied).Select(o => o.Setting).ToList();

                _logger.LogInformation(
                    "Configured {Mod}: {Applied} of {Total} settings in {File}",
                    config.ModId, applied.Count, config.Settings.Count, relative);

                outcomes.Add(new ModConfigOutcome(config.ModId, relative, applied, result.Unmatched));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not configure {Mod}", config.ModId);
                outcomes.Add(new ModConfigOutcome(config.ModId, null, [], config.Settings.Select(s => s.Name).ToList()));
            }
        }

        return new ConfigInstallReport(outcomes);
    }

    /// <summary>
    /// Finds the first hint that resolves to a real file. A hint's directory is
    /// taken literally (Windows paths are case-insensitive) and its filename may
    /// be a glob.
    /// </summary>
    private static string? LocateFile(string gamePath, IReadOnlyList<string> hints)
    {
        foreach (var hint in hints)
        {
            var normalised = hint.Replace('/', Path.DirectorySeparatorChar);
            var directory = Path.GetDirectoryName(normalised) ?? string.Empty;
            var pattern = Path.GetFileName(normalised);

            var searchDir = Path.Combine(gamePath, directory);
            if (!Directory.Exists(searchDir))
            {
                continue;
            }

            var match = Directory
                .EnumerateFiles(searchDir, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.Length)
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
