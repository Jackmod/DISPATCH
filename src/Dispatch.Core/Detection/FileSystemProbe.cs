using Microsoft.Win32;

namespace Dispatch.Core.Detection;

/// <summary>
/// The filesystem and registry facts the locator needs, behind an interface so
/// detection can be tested without a real Steam or Epic install present.
/// </summary>
public interface IFileSystemProbe
{
    /// <summary>Steam's install folder, or null when Steam is not installed.</summary>
    string? SteamPath { get; }

    /// <summary>The Epic manifests folder, or null.</summary>
    string? EpicManifestsPath { get; }

    /// <summary>Common paths GTA V is installed to when no launcher is involved.</summary>
    IReadOnlyList<string> CommonPaths { get; }

    /// <summary>Reads a text file, or null when it is missing or unreadable.</summary>
    string? ReadText(string path);

    /// <summary>Reads every <c>.item</c> manifest in a folder.</summary>
    IEnumerable<string> ReadAllManifests(string folder);

    /// <summary>Whether a directory exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>Whether a file exists.</summary>
    bool FileExists(string path);

    /// <summary>Resolves a path through any junction or symlink to its real target.</summary>
    string ResolveRealPath(string path);
}

/// <summary>The real probe: registry and disk.</summary>
public sealed class RealFileSystemProbe : IFileSystemProbe
{
    /// <inheritdoc />
    public string? SteamPath
    {
        get
        {
            // The registry key is the reliable source; Steam can live anywhere.
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public string? EpicManifestsPath
    {
        get
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var path = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            return Directory.Exists(path) ? path : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CommonPaths
    {
        get
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return
            [
                Path.Combine(programFiles, "Rockstar Games", "Grand Theft Auto V"),
                Path.Combine(programFilesX86, "Rockstar Games", "Grand Theft Auto V"),
                Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Grand Theft Auto V"),
                @"C:\Games\Grand Theft Auto V",
            ];
        }
    }

    /// <inheritdoc />
    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> ReadAllManifests(string folder)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.item");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            var text = ReadText(file);
            if (text is not null)
            {
                yield return text;
            }
        }
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string ResolveRealPath(string path)
    {
        // A moved library is often a junction; validating the real target
        // rather than the link is what keeps a later install writing to the
        // right disk.
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget is { } target ? Path.GetFullPath(target) : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
