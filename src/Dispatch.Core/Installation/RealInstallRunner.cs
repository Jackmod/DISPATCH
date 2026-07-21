using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using Dispatch.Core.Configuration;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using Dispatch.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Installation;

/// <summary>
/// The real install runner: fetches every mod a source can reach, unpacks it,
/// and places it into the game folder, journalled and reversible.
/// </summary>
/// <remarks>
/// This is the top of the install pipeline the whole engine was built for. It
/// reads the chosen preset from the catalogue, acquires each mod through
/// <see cref="Acquirer"/> (download to the archive cache, extract to staging),
/// and hands the staged folders to <see cref="LocalInstallRunner"/>, which does
/// the dangerous part — backing up, placing, journalling, recording.
///
/// <para>
/// A mod that has no automated source (LSPDFR itself and most plugins live
/// behind a login) is not a failure of the run: it is reported as needing
/// attention with a reason a person can act on, and the rest of the install
/// proceeds. A mod whose download or archive fails is reported the same way. The
/// run only throws if the game folder cannot be written at all, at which point
/// the journal already holds enough to roll back.
/// </para>
///
/// <para>
/// The seven-phase progress the UI shows is mapped onto the real work:
/// <see cref="InstallPhase.Collecting"/> spans acquisition,
/// <see cref="InstallPhase.PlacingFiles"/> spans placement. The phases the engine
/// does not model separately (textures via OpenIV) are simply not emitted, and
/// the UI handles their absence.
/// </para>
/// </remarks>
public sealed class RealInstallRunner : IInstallRunner
{
    private readonly Acquirer _acquirer;
    private readonly LocalInstallRunner _placer;
    private readonly ConfigInstaller _config;
    private readonly IAppPaths _paths;
    private readonly IRunIdFactory _runIds;
    private readonly ILogger<RealInstallRunner> _logger;

    /// <summary>Constructs the runner.</summary>
    public RealInstallRunner(
        Acquirer acquirer,
        LocalInstallRunner placer,
        ConfigInstaller config,
        IAppPaths paths,
        IRunIdFactory runIds,
        ILogger<RealInstallRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(acquirer);
        ArgumentNullException.ThrowIfNull(placer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(runIds);
        ArgumentNullException.ThrowIfNull(logger);

        _acquirer = acquirer;
        _placer = placer;
        _config = config;
        _paths = paths;
        _runIds = runIds;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InstallReport> RunAsync(
        InstallRequest request,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        if (string.IsNullOrWhiteSpace(request.GamePath) || !Directory.Exists(request.GamePath))
        {
            throw new DirectoryNotFoundException(
                $"The game folder '{request.GamePath}' does not exist. Locate the game before installing.");
        }

        var start = TimeProvider.System.GetTimestamp();
        var runId = _runIds.NewRunId();
        var staging = new StagingArea(_paths.StagingRoot, runId);

        var mods = ResolveMods(request);
        if (mods.Count == 0)
        {
            _logger.LogWarning(
                "Nothing to install: preset '{Preset}' with {Selected} explicit id(s) resolved to no mods",
                request.PresetId, request.ModIds?.Count ?? 0);
        }

        var total = mods.Count;
        var index = 0;

        var staged = new List<LocalInstallRunner.StagedMod>();
        var problems = new List<InstallProblem>();
        var skipped = new List<InstallProblem>();

        // ===== Collecting ================================================
        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            if (!_acquirer.CanAcquire(mod))
            {
                // Not in the pack and no automated source. Not an error — the
                // archive just needs to be dropped into the mod pack once. The id
                // is named so the completion screen can say exactly which folder.
                skipped.Add(new InstallProblem(
                    mod.Name,
                    $"{mod.Name} was not found in the mod pack and cannot be downloaded automatically. "
                    + $"Drop its archive into the mod pack under the '{mod.Id}' folder and run the install again."));
                Report(progress, InstallPhase.Collecting, $"{mod.Name} — needs manual download",
                    index, total, index, total, $"skip     {mod.Id} (manual source)");
                continue;
            }

            Report(progress, InstallPhase.Collecting, $"{mod.Name} — downloading",
                index, total, index, total, $"fetch    {mod.Id}");

            try
            {
                var downloadProgress = new Progress<DownloadProgress>(dp =>
                    Report(progress, InstallPhase.Collecting,
                        $"{mod.Name} — {Describe(dp)}", index, total, index, total));

                var acquired = await _acquirer
                    .AcquireAsync(mod, staging, downloadProgress, cancellationToken)
                    .ConfigureAwait(false);

                staged.Add(new LocalInstallRunner.StagedMod(mod, acquired.StagedFolder));

                var origin = acquired.FromCache ? "cache" : acquired.ResolvedVersion ?? "downloaded";
                Report(progress, InstallPhase.Collecting, $"{mod.Name} — staged",
                    index, total, index, total, $"staged   {mod.Id} ({origin})");
            }
            catch (AcquisitionException ex)
            {
                _logger.LogWarning(ex, "Could not acquire {Mod}", mod.Id);
                problems.Add(new InstallProblem(mod.Name, ex.Reason));
                Report(progress, InstallPhase.Collecting, $"{mod.Name} — failed",
                    index, total, index, total, $"error    {mod.Id}: {ex.Reason}");
            }
        }

        // ===== Placing ===================================================
        InstallRecord? record = null;
        if (staged.Count > 0)
        {
            Report(progress, InstallPhase.BackingUp, "Backing up files about to be replaced",
                total, total, total, total, "backup   preparing");

            record = await _placer
                .RunAsync(runId, request.GamePath, request.PresetId, request.GameBuild, staged, progress, cancellationToken)
                .ConfigureAwait(false);

            // ===== Writing configuration =================================
            // Every placed mod's config file is now on disk; write the guide's
            // values into each. Failures here never fail the install — a mod on
            // its defaults still loads.
            Report(progress, InstallPhase.WritingConfiguration, "Applying keybinds and settings",
                total, total, total, total, "config   applying guide values");

            var officer = request.Officer ?? OfficerValues.Default;
            var configReport = await _config
                .ApplyAsync(request.GamePath, staged.Select(s => s.Mod.Id), officer, cancellationToken)
                .ConfigureAwait(false);

            // ===== The safeguard: read every ini back and confirm it stuck =====
            // An independent second pass. If a value did not actually take, it is
            // reported as needing attention rather than silently wrong in-game.
            Report(progress, InstallPhase.Verifying, "Checking the config actually applied",
                total, total, total, total, "verify   reading every config file back");

            var verify = await _config
                .VerifyAsync(request.GamePath, staged.Select(s => s.Mod.Id), officer, cancellationToken)
                .ConfigureAwait(false);

            foreach (var (modId, file, check) in verify.Mismatches)
            {
                problems.Add(new InstallProblem(
                    $"{modId} configuration",
                    $"'{check.Setting}' in {file} should be '{check.Expected}' but is '{check.Actual}'. "
                    + "The setting did not apply; you can fix it in Settings, or reinstall this mod."));
            }

            Report(progress, InstallPhase.Verifying, "Verifying placed files",
                total, total, total, total,
                $"verify   {verify.VerifiedCount} setting(s) confirmed, {verify.Mismatches.Count} not applied; "
                + $"{record.Files.Count} file(s) placed");
        }

        // Staging is scratch; a clean run purges it. It is kept only when a
        // placement failure would want it for diagnosis, and placement throws
        // rather than returns in that case, so reaching here means success.
        staging.Purge();

        var elapsed = TimeProvider.System.GetElapsedTime(start);
        var installedNames = staged.Select(s => s.Mod.Name).ToList();

        _logger.LogInformation(
            "Install run {Run}: {Installed} installed, {Problems} failed, {Skipped} manual, {Files} file(s) placed",
            runId, installedNames.Count, problems.Count, skipped.Count, record?.Files.Count ?? 0);

        return new InstallReport(installedNames, problems, skipped, elapsed);
    }

    /// <summary>
    /// The mods to install: the explicit selection when the request carries one,
    /// otherwise the whole preset. An explicit selection is the "only what you
    /// picked" guarantee — nothing outside it is ever unpacked.
    /// </summary>
    private static IReadOnlyList<ModDefinition> ResolveMods(InstallRequest request)
    {
        if (request.ModIds is { Count: > 0 } ids)
        {
            return ids
                .Where(ModCatalogue.Mods.ContainsKey)
                .Select(id => ModCatalogue.Mods[id])
                .OrderBy(m => m.Order)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
        }

        return ModCatalogue.ModsFor(request.PresetId);
    }

    private static void Report(
        IProgress<InstallProgress> progress,
        InstallPhase phase,
        string detail,
        int completed,
        int total,
        int step,
        int steps,
        string? log = null)
    {
        var fraction = steps == 0 ? 0 : (double)step / steps;
        progress.Report(new InstallProgress(phase, detail, completed, total, fraction, log));
    }

    private static string Describe(DownloadProgress dp)
    {
        if (dp.Fraction is { } fraction)
        {
            return $"downloading {(int)Math.Round(fraction * 100)}%";
        }

        var mb = dp.BytesReceived / (1024d * 1024d);
        return $"downloading {mb:0.0} MB";
    }
}

/// <summary>Supplies a fresh run identifier. An interface so tests can pin it.</summary>
public interface IRunIdFactory
{
    /// <summary>A new, unique run identifier.</summary>
    string NewRunId();
}

/// <summary>Run ids as short sortable tokens: a UTC timestamp plus a random suffix.</summary>
/// <remarks>
/// Timestamp-prefixed so the runs directory sorts chronologically in a file
/// browser, with a random tail so two runs in the same second never collide.
/// </remarks>
public sealed class RunIdFactory : IRunIdFactory
{
    /// <inheritdoc />
    public string NewRunId()
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"run-{stamp}-{suffix}";
    }
}
