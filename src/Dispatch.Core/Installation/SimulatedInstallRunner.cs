using System.Diagnostics;

namespace Dispatch.Core.Installation;

/// <summary>
/// Plays through the seven phases against a clock, writing nothing.
/// </summary>
/// <remarks>
/// This exists so the install screen can be built and judged before any
/// file-writing code exists, which is the order the build plan calls for: the
/// resilience layer lands before anything touches a game folder.
///
/// <para>
/// It is deliberately not uniform. Phases take different lengths, the item
/// counter only advances during the phases that work per-mod, and one mod is
/// reported as needing attention — because a run where everything succeeds at
/// an even pace is the one case the real UI will almost never be in, and
/// designing against it hides every interesting state.
/// </para>
///
/// <para>
/// Named for what it is. A class called <c>InstallRunner</c> that silently did
/// nothing would be a genuinely dangerous thing to leave lying in this
/// codebase.
/// </para>
/// </remarks>
public sealed class SimulatedInstallRunner : IInstallRunner
{
    private readonly TimeSpan _tick;

    /// <summary>Constructs the simulation.</summary>
    /// <param name="tick">
    /// Base delay per step. The default runs a full pass in roughly 40 seconds,
    /// which is long enough to see the slideshow cycle and the tips rotate.
    /// </param>
    public SimulatedInstallRunner(TimeSpan? tick = null) =>
        _tick = tick ?? TimeSpan.FromMilliseconds(240);

    /// <summary>Weighting per phase, so the run does not feel metronomic.</summary>
    private static readonly (InstallPhase Phase, int Steps, bool CountsItems)[] Plan =
    [
        (InstallPhase.Collecting, 14, true),
        (InstallPhase.CheckingCompatibility, 4, false),
        (InstallPhase.BackingUp, 3, false),
        (InstallPhase.PlacingFiles, 16, true),
        (InstallPhase.WritingConfiguration, 8, false),
        (InstallPhase.InstallingTextures, 3, false),
        (InstallPhase.Verifying, 5, false),
    ];

    private static readonly string[] SampleMods =
    [
        "LSPDFR", "Script Hook V", "Script Hook V .NET", "RageNativeUI", "LemonUI",
        "Callout Interface", "Stop The Ped", "Grammar Police", "Ultimate Backup",
        "Better Chases+", "Simple Trainer", "ELS", "Spotlight", "Dash Cam V",
        "Heli Assistance", "Charges & Citations", "Simple HUD", "LIAR",
        "Radio Realism", "Search Items Reborn", "Clear The Way V",
    ];

    /// <inheritdoc />
    public async Task<InstallReport> RunAsync(
        InstallRequest request,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        var stopwatch = Stopwatch.StartNew();
        var installed = new List<string>();
        var total = Math.Max(1, request.ModCount);
        var completed = 0;

        foreach (var (phase, steps, countsItems) in Plan)
        {
            for (var step = 0; step < steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mod = SampleMods[(completed + step) % SampleMods.Length];

                if (countsItems && completed < total)
                {
                    completed++;
                    installed.Add(mod);
                }

                progress.Report(new InstallProgress(
                    phase,
                    DetailFor(phase, mod),
                    Math.Min(completed, total),
                    total));

                // Collecting is the slow phase in a real run; everything else
                // is disk-bound and comparatively quick.
                var multiplier = phase == InstallPhase.Collecting ? 2.0 : 1.0;
                await Task.Delay(_tick * multiplier, cancellationToken).ConfigureAwait(false);
            }
        }

        stopwatch.Stop();

        // One realistic failure, so the completion report is never designed
        // against a run where nothing went wrong.
        var needsAttention = new List<InstallProblem>
        {
            new("Radio Realism",
                "The download returned a web page instead of an archive, which usually means the "
                + "host was rate limiting. Its page has been left open so it can be fetched by hand."),
        };

        // Excluded by set rather than by List.Remove, which drops only the
        // first occurrence — mods repeat across phases, so a failed mod would
        // otherwise survive into the installed list and be reported twice.
        var failed = needsAttention.Select(problem => problem.Mod).ToHashSet(StringComparer.Ordinal);

        return new InstallReport(
            installed.Distinct(StringComparer.Ordinal)
                .Where(mod => !failed.Contains(mod))
                .ToList(),
            needsAttention,
            [],
            stopwatch.Elapsed);
    }

    private static string DetailFor(InstallPhase phase, string mod) => phase switch
    {
        InstallPhase.Collecting => $"{mod} — downloading",
        InstallPhase.CheckingCompatibility => $"{mod} — reading version",
        InstallPhase.BackingUp => "Backing up files about to be replaced",
        InstallPhase.PlacingFiles => $"{mod} — extracting",
        InstallPhase.WritingConfiguration => $"{mod} — writing config",
        InstallPhase.InstallingTextures => "Installing textures via OpenIV",
        InstallPhase.Verifying => $"{mod} — verifying hash",
        _ => mod,
    };
}
