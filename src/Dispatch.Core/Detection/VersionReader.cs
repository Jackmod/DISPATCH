using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Detection;

/// <summary>The versions that matter, read off an installation.</summary>
/// <param name="GameBuild">The GTA V build, for example <c>1.0.3725</c>. Null when unreadable.</param>
/// <param name="ScriptHookV">Script Hook V version, or null when not installed.</param>
/// <param name="ScriptHookVDotNet">Script Hook V .NET version, or null.</param>
/// <param name="HasModFiles">Whether any mod files were detected in the folder.</param>
public sealed record InstalledVersions(
    string? GameBuild,
    string? ScriptHookV,
    string? ScriptHookVDotNet,
    bool HasModFiles)
{
    /// <summary>True when the game build was read successfully.</summary>
    public bool IsGameReadable => GameBuild is not null;
}

/// <summary>Reads version resources off the game and its components.</summary>
public interface IVersionReader
{
    /// <summary>Reads everything knowable from a game folder.</summary>
    InstalledVersions Read(string gamePath);

    /// <summary>Reads the four-part version off a single file, or null.</summary>
    string? ReadFileVersion(string filePath);
}

/// <summary>
/// Reads versions from a game folder.
/// </summary>
/// <remarks>
/// The game build is the number everything else is measured against, and it
/// lives in the version resource on <c>GTA5.exe</c> rather than in any text
/// file — so it is read directly rather than scraped, which is both faster and
/// not fooled by a stale log.
///
/// <para>
/// The mod-file scan is what turns "verified" into "already modified" on the
/// locate screen. It looks for the specific files that only a mod install
/// leaves behind, named so the screen can say exactly what it found rather
/// than a vague warning.
/// </para>
/// </remarks>
public sealed class VersionReader : IVersionReader
{
    private readonly ILogger<VersionReader> _logger;

    /// <summary>Files whose presence means the folder has already been modded.</summary>
    public static readonly IReadOnlyList<string> ModMarkers =
    [
        "dinput8.dll",
        "ScriptHookV.dll",
        "ScriptHookVDotNet.asi",
        "RagePluginHook.exe",
        "LSPD First Response.dll",
    ];

    /// <summary>Constructs the reader.</summary>
    public VersionReader(ILogger<VersionReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public InstalledVersions Read(string gamePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var build = ReadGameBuild(Path.Combine(gamePath, "GTA5.exe"));
        var shv = ReadFileVersion(Path.Combine(gamePath, "ScriptHookV.dll"));
        var shvdn = ReadFileVersion(Path.Combine(gamePath, "ScriptHookVDotNet.asi"));
        var hasMods = HasAnyModFiles(gamePath);

        return new InstalledVersions(build, shv, shvdn, hasMods);
    }

    /// <inheritdoc />
    public string? ReadFileVersion(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);

            // Prefer the four-part file version; fall back to product version,
            // which some builds populate instead.
            if (info.FileMajorPart != 0 || info.FileMinorPart != 0 ||
                info.FileBuildPart != 0 || info.FilePrivatePart != 0)
            {
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
            }

            return string.IsNullOrWhiteSpace(info.ProductVersion) ? null : info.ProductVersion.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            _logger.LogDebug(ex, "Could not read version of {File}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Reads the GTA V build, normalised to the <c>1.0.NNNN</c> form the
    /// ecosystem quotes.
    /// </summary>
    private string? ReadGameBuild(string exePath)
    {
        if (!File.Exists(exePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);

            // GTA5.exe reports its build in the private part: 1.0.<build>.0.
            // The community writes it as 1.0.<build>, so the trailing .0 is
            // dropped rather than shown.
            if (info.FileMajorPart == 1 && info.FileMinorPart == 0)
            {
                return $"1.0.{info.FileBuildPart}";
            }

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the game build from {Exe}", exePath);
            return null;
        }
    }

    /// <summary>The mod markers actually present in the folder, named.</summary>
    public IReadOnlyList<string> FindModFiles(string gamePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var found = new List<string>();

        foreach (var marker in ModMarkers)
        {
            if (File.Exists(Path.Combine(gamePath, marker)))
            {
                found.Add(marker);
            }
        }

        // A plugins folder is the other unmistakable sign.
        if (Directory.Exists(Path.Combine(gamePath, "plugins")))
        {
            found.Add("plugins/");
        }

        return found;
    }

    private bool HasAnyModFiles(string gamePath) => FindModFiles(gamePath).Count > 0;
}
