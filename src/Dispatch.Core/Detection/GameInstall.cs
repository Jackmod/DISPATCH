namespace Dispatch.Core.Detection;

/// <summary>Which launcher a copy of the game came from.</summary>
public enum GamePlatform
{
    /// <summary>Found through Steam's library folders.</summary>
    Steam,

    /// <summary>Found through an Epic Games Launcher manifest.</summary>
    Epic,

    /// <summary>Found at a common path, launcher unknown.</summary>
    Rockstar,

    /// <summary>Chosen by hand.</summary>
    Manual,
}

/// <summary>One GTA V installation found on disk.</summary>
/// <param name="Path">Root folder, resolved through any junction to its real location.</param>
/// <param name="Platform">Which launcher it belongs to.</param>
public sealed record GameInstall(string Path, GamePlatform Platform)
{
    /// <summary>Full path to the game executable.</summary>
    public string ExecutablePath => System.IO.Path.Combine(Path, "GTA5.exe");

    /// <summary>True when the executable is actually present.</summary>
    public bool HasExecutable => File.Exists(ExecutablePath);
}
