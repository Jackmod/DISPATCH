namespace Dispatch.Core.Detection;

/// <summary>The verdict on one component's compatibility.</summary>
public enum Compatibility
{
    /// <summary>Fine.</summary>
    Ok,

    /// <summary>Will not work until it is addressed.</summary>
    Incompatible,

    /// <summary>Could not be determined.</summary>
    Unknown,
}

/// <summary>One compatibility finding.</summary>
/// <param name="Component">What was checked.</param>
/// <param name="Verdict">The result.</param>
/// <param name="Detail">Plain-language explanation.</param>
public sealed record CompatibilityFinding(string Component, Compatibility Verdict, string Detail);

/// <summary>
/// Checks whether the installed components will actually work against the game.
/// </summary>
/// <remarks>
/// The check is deliberately narrow, as the spec insists. Only a handful of
/// files care what GTA build is installed — Script Hook V and Script Hook V .NET
/// are locked to it — and everything else keys off the LSPDFR API instead. A
/// Rockstar patch does not invalidate Stop The Ped, and crying wolf about forty
/// plugins on every game update is exactly the noise this avoids.
///
/// <para>
/// The most useful thing it does is check <em>before</em> installing: read the
/// build a Script Hook V archive targets and say plainly whether it matches,
/// rather than letting the user find out from a silent crash after a
/// fifteen-minute install.
/// </para>
/// </remarks>
public static class CompatibilityChecker
{
    /// <summary>
    /// Checks a Script Hook V build against the game build.
    /// </summary>
    /// <param name="gameBuild">The game's build, e.g. <c>1.0.3725</c>.</param>
    /// <param name="scriptHookSupportedBuild">The build Script Hook V supports, from its own version or log.</param>
    public static CompatibilityFinding CheckScriptHook(string? gameBuild, string? scriptHookSupportedBuild)
    {
        if (gameBuild is null || scriptHookSupportedBuild is null)
        {
            return new CompatibilityFinding("Script Hook V", Compatibility.Unknown,
                "Could not read one of the versions to compare.");
        }

        if (string.Equals(gameBuild, scriptHookSupportedBuild, StringComparison.Ordinal))
        {
            return new CompatibilityFinding("Script Hook V", Compatibility.Ok,
                $"Built for {scriptHookSupportedBuild}, matching your game.");
        }

        return new CompatibilityFinding("Script Hook V", Compatibility.Incompatible,
            $"The Script Hook V you have is built for {scriptHookSupportedBuild}, but your game is "
            + $"{gameBuild}. Script Hook V is locked to the exact build, so this will not load.");
    }

    /// <summary>
    /// Checks an archive's Script Hook V build before it is installed.
    /// </summary>
    /// <remarks>
    /// This is the single most useful check in the app: catching a mismatch here
    /// costs a sentence, catching it after install costs a fifteen-minute run and
    /// a silent crash.
    /// </remarks>
    public static CompatibilityFinding CheckArchiveBeforeInstall(string? gameBuild, string? archiveBuild)
    {
        var finding = CheckScriptHook(gameBuild, archiveBuild);

        if (finding.Verdict != Compatibility.Incompatible)
        {
            return finding;
        }

        return finding with
        {
            Detail = $"The Script Hook V archive you have is built for {archiveBuild} — your game is "
                + $"{gameBuild}. This won't load. Get the version that matches before installing.",
        };
    }

    /// <summary>
    /// Whether a plugin should even be checked against the game build.
    /// </summary>
    /// <remarks>
    /// Only build-locked components are. Everything else tracks the LSPDFR API,
    /// so reporting it against the game build would be crying wolf.
    /// </remarks>
    public static bool TracksGameBuild(Catalogue.CompatibilityAnchor anchor) =>
        anchor == Catalogue.CompatibilityAnchor.GameBuild;
}
