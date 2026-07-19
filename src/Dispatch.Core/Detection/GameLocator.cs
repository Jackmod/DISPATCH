using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Detection;

/// <summary>Finds GTA V installations.</summary>
public interface IGameLocator
{
    /// <summary>
    /// Finds every GTA V install the machine can see, de-duplicated and
    /// verified to actually contain the executable.
    /// </summary>
    IReadOnlyList<GameInstall> Locate();
}

/// <summary>
/// Locates GTA V through Steam, Epic and the common Rockstar paths.
/// </summary>
/// <remarks>
/// Every install found is returned, never just the first. Someone with a Steam
/// and an Epic copy who silently gets the wrong one is left with a failure that
/// is close to undiagnosable, so the choice is always the user's.
///
/// <para>
/// The parsers are the interesting part and are separated from the filesystem
/// so they can be tested against real fixture text. Valve's VDF and Epic's
/// manifest JSON both have quirks — doubled backslashes, a nested library
/// object keyed by index — that a naive read gets subtly wrong.
/// </para>
/// </remarks>
public sealed partial class GameLocator : IGameLocator
{
    private readonly ILogger<GameLocator> _logger;
    private readonly IFileSystemProbe _fs;

    /// <summary>Constructs the locator.</summary>
    public GameLocator(ILogger<GameLocator> logger, IFileSystemProbe? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _fs = fileSystem ?? new RealFileSystemProbe();
    }

    /// <inheritdoc />
    public IReadOnlyList<GameInstall> Locate()
    {
        var found = new List<GameInstall>();

        found.AddRange(FromSteam());
        found.AddRange(FromEpic());
        found.AddRange(FromCommonPaths());

        // De-duplicate by resolved path: the same folder can be reached through
        // Steam and a common path, and showing it twice is noise.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameInstall>();

        foreach (var install in found)
        {
            var real = _fs.ResolveRealPath(install.Path);
            if (seen.Add(real) && _fs.FileExists(Path.Combine(real, "GTA5.exe")))
            {
                result.Add(install with { Path = real });
            }
        }

        _logger.LogInformation("Located {Count} GTA V install(s)", result.Count);
        return result;
    }

    // ===== Steam ==========================================================

    private IEnumerable<GameInstall> FromSteam()
    {
        var steam = _fs.SteamPath;
        if (steam is null)
        {
            yield break;
        }

        var librariesFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var vdf = _fs.ReadText(librariesFile);
        if (vdf is null)
        {
            yield break;
        }

        foreach (var library in ParseSteamLibraries(vdf))
        {
            // GTA V is Steam app 271590; its folder name is fixed.
            var candidate = Path.Combine(library, "steamapps", "common", "Grand Theft Auto V");
            if (_fs.DirectoryExists(candidate))
            {
                yield return new GameInstall(candidate, GamePlatform.Steam);
            }
        }
    }

    /// <summary>
    /// Extracts library paths from a <c>libraryfolders.vdf</c>.
    /// </summary>
    /// <remarks>
    /// Valve escapes backslashes in the VDF, so <c>D:\\Games</c> appears as
    /// <c>D:\\\\Games</c>. Reading the path without collapsing that yields a
    /// folder that does not exist. Public so the parsing is testable without a
    /// Steam install present.
    /// </remarks>
    public static IReadOnlyList<string> ParseSteamLibraries(string vdf)
    {
        ArgumentNullException.ThrowIfNull(vdf);

        var paths = new List<string>();

        foreach (Match match in SteamPathRegex().Matches(vdf))
        {
            var raw = match.Groups["path"].Value;
            var unescaped = raw.Replace("\\\\", "\\", StringComparison.Ordinal);
            paths.Add(unescaped);
        }

        return paths;
    }

    // ===== Epic ===========================================================

    private IEnumerable<GameInstall> FromEpic()
    {
        var manifests = _fs.EpicManifestsPath;
        if (manifests is null || !_fs.DirectoryExists(manifests))
        {
            yield break;
        }

        foreach (var manifest in _fs.ReadAllManifests(manifests))
        {
            var path = ParseEpicManifest(manifest);
            if (path is not null && _fs.DirectoryExists(path))
            {
                yield return new GameInstall(path, GamePlatform.Epic);
            }
        }
    }

    /// <summary>
    /// Reads the install location out of an Epic manifest, if it is GTA V.
    /// </summary>
    /// <remarks>
    /// Epic writes one manifest per installed game, and GTA V's carries the
    /// catalogue name <c>GTA5</c>. Matching on that avoids returning the folder
    /// of some other Epic game. Public for the same testing reason as the VDF
    /// parser.
    /// </remarks>
    public static string? ParseEpicManifest(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // A manifest that is not GTA V is skipped rather than mis-attributed.
        var isGta =
            json.Contains("\"GTA5\"", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase);

        if (!isGta)
        {
            return null;
        }

        var match = EpicLocationRegex().Match(json);
        if (!match.Success)
        {
            return null;
        }

        // JSON escapes backslashes too.
        return match.Groups["path"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    // ===== Common paths ===================================================

    private IEnumerable<GameInstall> FromCommonPaths()
    {
        foreach (var path in _fs.CommonPaths)
        {
            if (_fs.DirectoryExists(path))
            {
                yield return new GameInstall(path, GamePlatform.Rockstar);
            }
        }
    }

    [GeneratedRegex("""
        "path"\s*"(?<path>[^"]+)"
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SteamPathRegex();

    [GeneratedRegex("""
        "InstallLocation"\s*:\s*"(?<path>[^"]+)"
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex EpicLocationRegex();
}
