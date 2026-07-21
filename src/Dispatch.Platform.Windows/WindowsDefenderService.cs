using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Dispatch.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Dispatch.Platform.Windows;

/// <summary>
/// Adds a Windows Defender folder exclusion through PowerShell's Defender
/// cmdlets, elevating for the one command that needs it.
/// </summary>
/// <remarks>
/// <c>Add-MpPreference -ExclusionPath</c> requires administrator rights, so the
/// add is run through an elevated PowerShell (a UAC prompt the user accepts or
/// declines). The check, <c>Get-MpPreference</c>, does not need elevation, so the
/// UI can show whether the folder is already covered before offering to add it.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDefenderService : IDefenderService
{
    private readonly ILogger<WindowsDefenderService> _logger;

    /// <summary>Constructs the service.</summary>
    public WindowsDefenderService(ILogger<WindowsDefenderService> logger)
    {
        _logger = logger;
        IsAvailable = OperatingSystem.IsWindows();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public async Task<bool?> IsExcludedAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var script =
                "$p = (Get-MpPreference).ExclusionPath; " +
                $"if ($p -contains '{Escape(path)}') {{ 'yes' }} else {{ 'no' }}";

            var output = await RunPowerShellAsync(script, elevated: false, cancellationToken).ConfigureAwait(false);
            return output?.Trim() switch
            {
                "yes" => true,
                "no" => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            // Win32Exception covers PowerShell being missing or blocked by policy.
            _logger.LogDebug(ex, "Could not read Defender exclusions");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AddExclusionAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var script = $"Add-MpPreference -ExclusionPath '{Escape(path)}'";
            await RunPowerShellAsync(script, elevated: true, cancellationToken).ConfigureAwait(false);

            // Confirm it took, rather than trusting the exit code of an elevated
            // process we cannot capture output from.
            return await IsExcludedAsync(path, cancellationToken).ConfigureAwait(false) == true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            // Win32Exception is the one that actually happens here: declining the UAC
            // prompt raises ERROR_CANCELLED (1223) from ShellExecute. Treat every one
            // of these as "not added" rather than letting it crash the wizard.
            _logger.LogWarning(ex, "Could not add Defender exclusion for {Path}", path);
            return false;
        }
    }

    private static string Escape(string path) => path.Replace("'", "''");

    private static async Task<string?> RunPowerShellAsync(string script, bool elevated, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = elevated,     // ShellExecute is required to elevate
            CreateNoWindow = !elevated,
            RedirectStandardOutput = !elevated,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (elevated)
        {
            startInfo.Verb = "runas";       // triggers the UAC prompt
            startInfo.Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"";
        }
        else
        {
            startInfo.Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"";
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        string? output = null;
        if (!elevated)
        {
            output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return output;
    }
}
