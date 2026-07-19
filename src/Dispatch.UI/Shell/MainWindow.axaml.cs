using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Dispatch.UI.Shell;

/// <summary>The application window. Code-behind is for control wiring only.</summary>
public partial class MainWindow : Window
{
    /// <summary>Tracks whether the intro has run, so it is shown once per session.</summary>
    private static bool _introPlayed;

    /// <summary>Constructs the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ApplyDarkTitleBar();
        WireIntro();
    }

    private void WireIntro()
    {
        if (_introPlayed)
        {
            Intro.IsVisible = false;
            ShellHost.Opacity = 1;
            return;
        }

        _introPlayed = true;
        Intro.Completed += OnIntroCompleted;
    }

    private void OnIntroCompleted(object? sender, EventArgs e)
    {
        Intro.Completed -= OnIntroCompleted;
        Intro.IsVisible = false;

        // The shell fades up as the intro's own fade finishes, so the two
        // overlap into one movement rather than reading as two steps.
        ShellHost.Opacity = 1;
    }

    /// <summary>
    /// Asks DWM to render the title bar dark.
    /// </summary>
    /// <remarks>
    /// Avalonia's dark theme variant styles the client area but not the
    /// non-client frame, so a considered dark app ships with a bright white
    /// caption bar unless this is set. It is a cosmetic hint: on a build that
    /// does not recognise the attribute the call fails harmlessly and the frame
    /// stays light, which is why nothing here throws.
    ///
    /// This is the one Win32 call in the UI project. It stays here rather than
    /// behind a Core interface because it decorates this window rather than
    /// expressing any domain behaviour, and routing it through the platform
    /// project would mean an abstraction that exists for exactly one caller.
    /// </remarks>
    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 20 on Windows 10 1903+ and Windows 11; 19 on earlier 1809-era builds.
        const int UseImmersiveDarkMode = 20;
        const int UseImmersiveDarkModeLegacy = 19;

        var enabled = 1;
        if (NativeMethods.DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            NativeMethods.DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
    }
}
