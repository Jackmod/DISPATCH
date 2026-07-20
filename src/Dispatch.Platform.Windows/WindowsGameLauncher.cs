using System.Diagnostics;
using System.Runtime.Versioning;
using Dispatch.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Dispatch.Platform.Windows;

/// <summary>Starts RagePluginHook from the game folder.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameLauncher : IGameLauncher
{
    private static readonly string[] LoaderNames = ["RAGEPluginHook.exe", "RagePluginHook.exe"];

    private readonly ILogger<WindowsGameLauncher> _logger;

    /// <summary>Constructs the launcher.</summary>
    public WindowsGameLauncher(ILogger<WindowsGameLauncher> logger)
    {
        _logger = logger;
        IsAvailable = OperatingSystem.IsWindows();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public LaunchOutcome LaunchRagePluginHook(string gamePath)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(gamePath))
        {
            return LaunchOutcome.Unavailable;
        }

        var exe = LoaderNames
            .Select(name => Path.Combine(gamePath, name))
            .FirstOrDefault(File.Exists);

        if (exe is null)
        {
            _logger.LogInformation("RagePluginHook not found in {Path}", gamePath);
            return LaunchOutcome.LoaderNotFound;
        }

        try
        {
            // Started from the game folder so RagePluginHook finds the game and its
            // plugins beside it, exactly as launching it by hand would.
            // UseShellExecute honours the loader's own manifest, so if RagePluginHook
            // asks for administrator rights Windows elevates it — without Dispatch
            // forcing a UAC prompt on a build that does not need one.
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = gamePath,
                UseShellExecute = true,
            });

            _logger.LogInformation("Launched RagePluginHook from {Exe}", exe);
            return LaunchOutcome.Launched;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not start RagePluginHook");
            return LaunchOutcome.Failed;
        }
    }
}
