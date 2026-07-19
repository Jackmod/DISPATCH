namespace Dispatch.Core.Resilience;

/// <summary>One thing the user can do about an error.</summary>
/// <param name="Label">The button text.</param>
/// <param name="Command">A stable identifier the UI maps to an actual handler.</param>
public sealed record ErrorAction(string Label, string Command);

/// <summary>A known failure, in language a person can read, with a way out.</summary>
/// <param name="Code">Stable identifier, also what a support conversation quotes.</param>
/// <param name="Title">One line naming what happened.</param>
/// <param name="Body">Why it happened and what it means, no jargon, no stack trace.</param>
/// <param name="Actions">What the user can do, most useful first.</param>
public sealed record ErrorSpec(string Code, string Title, string Body, IReadOnlyList<ErrorAction> Actions);

/// <summary>
/// Maps every known failure to a plain-language explanation and a remedy.
/// </summary>
/// <remarks>
/// No exception message ever reaches the user. This is the whole reason: an
/// install touches an antivirus product, a filesystem and thirty config files,
/// and every failure it can produce has a specific, human cause that a stack
/// trace hides. The wording here is written to be read by someone mid-panic
/// with a half-broken game, and by the person they paste the report to
/// afterwards.
///
/// <para>
/// An unmapped error is not a crash: <see cref="Generic"/> gives it a card with
/// the run id and a copy-diagnostics action, and the full trace goes to the log.
/// </para>
/// </remarks>
public static class ErrorCatalogue
{
    private static readonly IReadOnlyDictionary<string, ErrorSpec> Specs = Build();

    /// <summary>Looks up a spec by code, or a generic card when it is unmapped.</summary>
    public static ErrorSpec Resolve(string code, string? runId = null) =>
        Specs.TryGetValue(code, out var spec) ? spec : Generic(runId);

    /// <summary>Every known code, for tests and the docs.</summary>
    public static IReadOnlyCollection<string> Codes => Specs.Keys.ToList();

    /// <summary>Whether a code is mapped.</summary>
    public static bool IsKnown(string code) => Specs.ContainsKey(code);

    private static ErrorSpec Generic(string? runId) => new(
        "UNKNOWN",
        "Something went wrong that Dispatch did not expect",
        runId is null
            ? "The full details are in the log. Copy the diagnostics and they can be looked at."
            : $"The full details are in the log for run {runId}. Copy the diagnostics and they can be looked at.",
        [new ErrorAction("Copy diagnostics", "copy-diagnostics")]);

    private static IReadOnlyDictionary<string, ErrorSpec> Build() => new Dictionary<string, ErrorSpec>(StringComparer.Ordinal)
    {
        ["AV_QUARANTINE"] = new(
            "AV_QUARANTINE",
            "Your antivirus removed a required file",
            "ScriptHookV.dll downloaded successfully but has since been deleted. Antivirus products "
            + "flag it because it injects into the game — which is exactly what it is for. This is a "
            + "false positive, and the install cannot finish until the file is allowed to stay.",
            [
                new ErrorAction("Add exclusion and retry", "av-exclude-retry"),
                new ErrorAction("Show me how", "av-help"),
            ]),

        ["BUILD_MISMATCH"] = new(
            "BUILD_MISMATCH",
            "Script Hook V does not match your game",
            "The Script Hook V you have is built for a different game build than the one installed. "
            + "Script Hook V is locked to the exact build, so nothing will load until they match. This "
            + "is behind almost every 'LSPDFR stopped working' report.",
            [
                new ErrorAction("Get the matching version", "open-shv-page"),
                new ErrorAction("Roll the game back", "rollback-help"),
            ]),

        ["GAME_RUNNING"] = new(
            "GAME_RUNNING",
            "GTA V is still running",
            "The game, its launcher or RagePluginHook is open, and files cannot be replaced while they "
            + "are in use. Close them and the install can continue.",
            [new ErrorAction("Check again", "recheck-processes")]),

        ["NO_NETWORK"] = new(
            "NO_NETWORK",
            "No internet connection",
            "Dispatch fetches every mod from its author's server, so it needs a connection. Rather than "
            + "start a run that cannot finish, it stopped here.",
            [new ErrorAction("Try again", "recheck-network")]),

        ["DISK_FULL"] = new(
            "DISK_FULL",
            "Not enough disk space",
            "There is not enough room to download and stage the setup safely. Dispatch needs the "
            + "install size plus half again for staging, so nothing is placed until it has verified.",
            [new ErrorAction("Check space again", "recheck-disk")]),

        ["CORRUPT_ARCHIVE"] = new(
            "CORRUPT_ARCHIVE",
            "A download arrived damaged",
            "One archive did not survive the download intact. Dispatch re-fetched it once and it was "
            + "still damaged, so it has been set aside. The rest of the install continued without it.",
            [new ErrorAction("Try this one again", "refetch-mod")]),

        ["HTML_NOT_ARCHIVE"] = new(
            "HTML_NOT_ARCHIVE",
            "A download returned a web page, not a file",
            "The server sent an error page where an archive should have been — usually rate limiting or "
            + "a gate the automatic flow could not clear. Its page has been left open so you can fetch "
            + "it by hand.",
            [new ErrorAction("Open the page", "open-mod-page")]),

        ["CONTROLLED_FOLDER"] = new(
            "CONTROLLED_FOLDER",
            "Windows blocked the install from writing",
            "Controlled Folder Access, a Windows Defender feature, is silently blocking writes to your "
            + "game folder. It surfaces as a bare permission error with no explanation. Adding Dispatch "
            + "to its allowed apps fixes it.",
            [
                new ErrorAction("Allow Dispatch and retry", "cfa-allow-retry"),
                new ErrorAction("Show me how", "cfa-help"),
            ]),

        ["NEEDS_ELEVATION"] = new(
            "NEEDS_ELEVATION",
            "The game folder needs administrator access",
            "Your game is installed somewhere that needs elevated permission to change, usually under "
            + "Program Files. Dispatch can relaunch with the access it needs and pick up where it left off.",
            [new ErrorAction("Relaunch as administrator", "relaunch-elevated")]),

        ["OPENIV_MISSING"] = new(
            "OPENIV_MISSING",
            "OpenIV is needed for three texture installs",
            "Three of the mods install textures through OpenIV, which is not present. Everything else "
            + "installed fine. You can install OpenIV and add these three, or skip them — the manual "
            + "steps are in the report.",
            [
                new ErrorAction("Install OpenIV", "install-openiv"),
                new ErrorAction("Skip the textures", "skip-textures"),
            ]),

        ["LAUNCHER_VERIFIED"] = new(
            "LAUNCHER_VERIFIED",
            "Your mod files were removed",
            "Files Dispatch placed are missing or back to stock. This almost always means Steam or Epic "
            + "verified the game files, which restores everything to vanilla. It can be put back in "
            + "seconds from the archives already downloaded.",
            [new ErrorAction("Reinstall from archives", "reinstall-from-cache")]),
    };
}
