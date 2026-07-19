namespace Dispatch.Core.Maintenance;

/// <summary>
/// Everything that belongs to a stock GTA V install, and everything that must
/// never be touched regardless of what else is decided.
/// </summary>
/// <remarks>
/// An allowlist, never a blocklist. A blocklist has to anticipate every file a
/// mod might create; an allowlist only has to know what shipped with the game,
/// which is a fixed and knowable set. Anything not on it is reported to the
/// user rather than acted on.
///
/// <para>
/// The protection rules are separate from the stock list and win over
/// everything. Saves and settings are not part of a stock install — they are
/// created afterwards — so a pure "is this stock?" test would happily offer to
/// delete somebody's career.
/// </para>
/// </remarks>
public static class StockManifest
{
    /// <summary>
    /// Files present in a stock install, at the game root.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively: Windows filesystems are, and a mod that
    /// writes <c>Gta5.exe</c> would otherwise slip past.
    /// </remarks>
    public static readonly IReadOnlySet<string> RootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GTA5.exe",
        "GTAVLauncher.exe",
        "PlayGTAV.exe",
        "GTAVLanguageSelect.exe",
        "commonData.rpf",
        "index.bin",
        "installscript.vdf",
        "steam_api64.dll",
        "GFSDK_ShadowLib.win64.dll",
        "GFSDK_TXAA.win64.dll",
        "GFSDK_TXAA_AlphaResolve.win64.dll",
        "NvPmApi.Core.win64.dll",
        "d3dcompiler_46.dll",
        "d3dcsx_46.dll",
        "bink2w64.dll",
        "version.txt",
        "EasyAntiCheat_launcher.exe",
    };

    /// <summary>Folders present in a stock install, relative to the game root.</summary>
    public static readonly IReadOnlySet<string> RootFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "update",
        "x64",
        "common",
        "ReadMe",
        "installers",
        "EasyAntiCheat",
        "Redistributables",
        "DLCPacks",
        "Launcher Files",
    };

    /// <summary>
    /// Paths that are never touched, whatever tier they land in.
    /// </summary>
    /// <remarks>
    /// Every entry here represents something a user cannot get back. They are
    /// checked before any other rule and there is no override for them in the
    /// UI, because a confirmation dialog is not a safety mechanism when the
    /// thing being confirmed is somebody's save file.
    /// </remarks>
    public static readonly IReadOnlyList<string> ProtectedSegments =
    [
        "profiles",       // in-game settings and keybinds
        "savegames",
        "user music",
        "rockstar games",
        "social club",
        "launcher files",
        "easyanticheat",
    ];

    /// <summary>File extensions that are never removed.</summary>
    public static readonly IReadOnlySet<string> ProtectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".sav",
        ".sgd",
        ".bak",
    };

    /// <summary>True when this relative path is part of a stock install.</summary>
    public static bool IsStock(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalised = Normalise(relativePath);
        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        // Anything inside a stock folder is stock, however deep.
        if (segments.Length > 1 && RootFolders.Contains(segments[0]))
        {
            return true;
        }

        return segments.Length == 1 && RootFiles.Contains(segments[0]);
    }

    /// <summary>
    /// True when this path must never be removed, whatever else is true of it.
    /// </summary>
    public static bool IsProtected(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalised = Normalise(relativePath);

        if (ProtectedExtensions.Contains(Path.GetExtension(normalised)))
        {
            return true;
        }

        // Segment-wise rather than substring: a mod called "profiles-plus"
        // should not inherit protection from the word "profiles", and a folder
        // legitimately named "profiles" anywhere in the tree should keep it.
        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            ProtectedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Lower-cases and forward-slashes a path so comparisons are stable.</summary>
    internal static string Normalise(string path) =>
        path.Replace('\\', '/').Trim('/').ToLowerInvariant();
}
