namespace Dispatch.Core.Detection;

/// <summary>Whether a required runtime is present.</summary>
public enum DependencyState
{
    /// <summary>Found — the mods that need it can load.</summary>
    Installed,

    /// <summary>Not found — something built on it will fail, usually silently.</summary>
    Missing,

    /// <summary>Could not tell (for example, not running on Windows).</summary>
    Unknown,
}

/// <summary>One runtime the mod stack depends on, and where to get it.</summary>
/// <param name="Name">What it is, named the way its vendor names it.</param>
/// <param name="Why">Which part of the setup needs it, in plain words.</param>
/// <param name="State">Whether it was found.</param>
/// <param name="DownloadUrl">The official download, shown only when it is missing.</param>
public sealed record DependencyStatus(string Name, string Why, DependencyState State, string DownloadUrl)
{
    /// <summary>True when this dependency is missing and should be fetched.</summary>
    public bool IsMissing => State == DependencyState.Missing;
}

/// <summary>Reports whether the runtimes the mod stack needs are installed.</summary>
public interface ISystemDependencyProbe
{
    /// <summary>Checks each dependency and reports its state.</summary>
    IReadOnlyList<DependencyStatus> Check();
}

/// <summary>
/// Detects the three runtimes a working LSPDFR install silently depends on — the
/// Visual C++ redistributable, the .NET Framework and the WebView2 runtime — so a
/// missing one is named here rather than surfacing as a black screen at launch.
/// </summary>
/// <remarks>
/// Detection is by file and folder presence, not the registry, so this compiles and
/// runs everywhere the rest of Core does; off Windows it reports nothing rather than
/// a wall of false warnings. Each check is deliberately conservative — it confirms the
/// runtime's files exist where the OS installs them, and leaves precise version
/// gating to the component that actually loads against it.
/// </remarks>
public sealed class SystemDependencyProbe : ISystemDependencyProbe
{
    /// <inheritdoc />
    public IReadOnlyList<DependencyStatus> Check()
    {
        // Off Windows these runtimes are not the concept they are on the target OS;
        // reporting them Missing would be noise, so nothing is reported at all.
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return
        [
            CheckVisualCpp(),
            CheckDotNetFramework(),
            CheckWebView2(),
        ];
    }

    private static DependencyStatus CheckVisualCpp()
    {
        // Script Hook V is a native library; without the Visual C++ runtime it fails
        // to load and nothing built on it comes up. The runtime drops vcruntime140
        // into System32, so its presence there is the signal.
        var system = Environment.SystemDirectory;
        var present = File.Exists(Path.Combine(system, "vcruntime140.dll"))
            && File.Exists(Path.Combine(system, "vcruntime140_1.dll"));

        return new DependencyStatus(
            "Visual C++ Redistributable (x64)",
            "Script Hook V is native code and won't load without it.",
            present ? DependencyState.Installed : DependencyState.Missing,
            "https://aka.ms/vs/17/release/vc_redist.x64.exe");
    }

    private static DependencyStatus CheckDotNetFramework()
    {
        // Script Hook V .NET v3 runs on the .NET Framework. The framework installs to
        // a fixed v4.0.30319 folder regardless of its 4.x point release, so the folder
        // existing confirms the framework is present — and every supported build of
        // Windows 10 and 11 ships 4.8, so on a modern OS this is effectively that.
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var present = Directory.Exists(
            Path.Combine(windir, "Microsoft.NET", "Framework64", "v4.0.30319"));

        return new DependencyStatus(
            ".NET Framework 4.8",
            "Script Hook V .NET needs it to run managed plugins.",
            present ? DependencyState.Installed : DependencyState.Missing,
            "https://dotnet.microsoft.com/download/dotnet-framework/net48");
    }

    private static DependencyStatus CheckWebView2()
    {
        // The WebView2 runtime installs a versioned folder under either Program Files
        // (machine-wide) or the user's local app data (per-user). It ships by default
        // on Windows 11 and most Windows 10, so it is usually present.
        var present = HasWebView2(Environment.GetEnvironmentVariable("ProgramFiles(x86)"))
            || HasWebView2(Environment.GetEnvironmentVariable("ProgramFiles"))
            || HasWebView2(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        return new DependencyStatus(
            "WebView2 Runtime",
            "Used by Dispatch's own download and help panels.",
            present ? DependencyState.Installed : DependencyState.Missing,
            "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
    }

    private static bool HasWebView2(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var application = Path.Combine(root, "Microsoft", "EdgeWebView", "Application");
        try
        {
            // A versioned subfolder (e.g. 120.0.2210.144) under Application means the
            // runtime is installed; an empty or absent folder means it is not.
            return Directory.Exists(application) && Directory.EnumerateDirectories(application).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
