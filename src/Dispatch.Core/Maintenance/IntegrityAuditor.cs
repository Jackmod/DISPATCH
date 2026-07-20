using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;

namespace Dispatch.Core.Maintenance;

/// <summary>How serious an audit finding is.</summary>
public enum AuditSeverity
{
    /// <summary>Everything is as it should be.</summary>
    Ok,

    /// <summary>Works, but something wants attention.</summary>
    Warning,

    /// <summary>Broken; the setup will not work correctly.</summary>
    Problem,
}

/// <summary>One thing the audit checked.</summary>
/// <param name="Severity">How serious.</param>
/// <param name="Title">One line naming it.</param>
/// <param name="Detail">Plain-language explanation.</param>
/// <param name="FixCommand">A stable command id the UI maps to a one-click fix, if one exists.</param>
public sealed record AuditFinding(AuditSeverity Severity, string Title, string Detail, string? FixCommand = null);

/// <summary>The whole audit result.</summary>
/// <param name="Findings">Every finding, worst first.</param>
public sealed record AuditReport(IReadOnlyList<AuditFinding> Findings)
{
    /// <summary>The worst severity present.</summary>
    public AuditSeverity Worst =>
        Findings.Count == 0 ? AuditSeverity.Ok : Findings.Max(f => f.Severity);

    /// <summary>A one-line verdict for the dashboard.</summary>
    public string Verdict => Worst switch
    {
        AuditSeverity.Problem => "Problems found",
        AuditSeverity.Warning => "Needs attention",
        _ => "All good",
    };

    /// <summary>Whether everything is fine.</summary>
    public bool IsHealthy => Worst == AuditSeverity.Ok;
}

/// <summary>
/// Checks an install against the record of what was placed.
/// </summary>
/// <remarks>
/// The single most useful check is file integrity: hash every file the install
/// record says was placed and compare. A file that is now missing or reverted
/// to a different hash means something external removed it — and in this
/// ecosystem that something is almost always Steam or Epic verifying the game
/// files, which restores everything to stock. Naming that cause directly is the
/// difference between a five-minute fix and a reinstall.
/// </remarks>
public sealed class IntegrityAuditor
{
    /// <summary>Runs the file-integrity audit against a record.</summary>
    /// <param name="gamePath">The game folder.</param>
    /// <param name="record">What the install placed.</param>
    /// <param name="currentBuild">The game build now, to compare against the record.</param>
    public async Task<AuditReport> AuditAsync(
        string gamePath,
        InstallRecord record,
        string? currentBuild,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(record);

        var findings = new List<AuditFinding>();

        if (!record.IsInstalled)
        {
            return new AuditReport([new AuditFinding(
                AuditSeverity.Ok, "Nothing installed yet", "Run the installer to set up your patrol.")]);
        }

        findings.AddRange(await CheckFileIntegrityAsync(gamePath, record, cancellationToken).ConfigureAwait(false));
        findings.Add(CheckGameBuild(record, currentBuild));

        // Worst first, so the dashboard leads with what matters.
        findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return new AuditReport(findings);
    }

    private static async Task<IReadOnlyList<AuditFinding>> CheckFileIntegrityAsync(
        string gamePath, InstallRecord record, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var changed = new List<string>();

        foreach (var placed in record.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(gamePath, placed.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                missing.Add(placed.RelativePath);
                continue;
            }

            var hash = await Hashing.Sha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, placed.Sha256, StringComparison.Ordinal))
            {
                changed.Add(placed.RelativePath);
            }
        }

        if (missing.Count == 0 && changed.Count == 0)
        {
            return [new AuditFinding(AuditSeverity.Ok, "All files present",
                $"Every one of the {record.Files.Count} installed files is intact.")];
        }

        var affected = missing.Count + changed.Count;

        // A large fraction of files gone at once is the signature of a launcher
        // verification, not a stray deletion, and the wording says so.
        var wholesale = affected >= record.Files.Count / 2;

        return
        [
            new AuditFinding(
                AuditSeverity.Problem,
                wholesale ? "Your mod files were removed" : $"{affected} file(s) are missing or changed",
                wholesale
                    ? "Most of your installed files are gone or back to stock. This almost always means "
                    + "Steam or Epic verified the game files, which wipes every mod. It can be put back in "
                    + "seconds from the archives already downloaded."
                    : $"{missing.Count} missing, {changed.Count} changed. Something outside Dispatch altered them.",
                FixCommand: "reinstall-from-cache"),
        ];
    }

    private static AuditFinding CheckGameBuild(InstallRecord record, string? currentBuild)
    {
        if (currentBuild is null || record.GameBuild is null)
        {
            return new AuditFinding(AuditSeverity.Warning, "Could not read the game build",
                "Dispatch could not confirm the game build against the install.");
        }

        if (string.Equals(currentBuild, record.GameBuild, StringComparison.Ordinal))
        {
            return new AuditFinding(AuditSeverity.Ok, "Game build matches",
                $"Still on {currentBuild}, the build you installed against.");
        }

        // The most direct wording available, as the spec asks for.
        return new AuditFinding(
            AuditSeverity.Problem,
            "Rockstar updated GTA V",
            $"The game is now {currentBuild}; you installed against {record.GameBuild}. Script Hook V is "
            + "locked to the exact build, so it no longer matches — this is why LSPDFR stopped working.",
            FixCommand: "open-shv-page");
    }
}
