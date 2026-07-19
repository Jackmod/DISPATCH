using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dispatch.UI.Launcher;
using Dispatch.UI.Wizard;

namespace Dispatch.UI.Shell;

/// <summary>The application window. Code-behind is for control wiring only.</summary>
public partial class MainWindow : Window
{
    /// <summary>Tracks whether the intro has run, so it is shown once per session.</summary>
    private static bool _introPlayed;

    /// <summary>Constructs the window.</summary>
    public MainWindow()
        : this(new WizardViewModel())
    {
    }

    /// <summary>Constructs the window against a composed wizard.</summary>
    public MainWindow(WizardViewModel wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);

        InitializeComponent();
        ApplyDarkTitleBar();
        WireIntro();

        Wizard.DataContext = wizard;

        // Finishing the wizard drops into the launcher, which is the real
        // handoff rather than the F10 development shortcut.
        wizard.Completed += OnWizardCompleted;

        // Tunnelling, not bubbling: Avalonia's directional focus navigation
        // consumes arrow keys on the way down, so a bubbling handler never
        // sees Ctrl+Arrow â€” focus just moves instead.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnWizardCompleted(object? sender, EventArgs e)
    {
        var officer = sender is WizardViewModel wizard
            ? await wizard.BuildOfficerAsync().ConfigureAwait(true)
            : null;

        LauncherShell.DataContext = new LauncherViewModel(officer);
        LauncherShell.IsVisible = true;
        Wizard.IsVisible = false;
    }

    /// <summary>
    /// Development shortcuts. None of these are reachable from shipped
    /// navigation, and all of them are compiled out of a Release build.
    /// </summary>
    /// <remarks>
    /// F12 toggles the design-system gallery.
    ///
    /// <para>
    /// Ctrl+Right and Ctrl+Left step the wizard regardless of whether the
    /// current screen is satisfied. Without this the install screen is a
    /// dead end â€” it hides its own navigation by design, and nothing advances
    /// it until InstallRunner exists â€” so the last screen would be unreachable
    /// and untestable.
    /// </para>
    /// </remarks>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
#if DEBUG
        if (e.Key == Key.F12)
        {
            Gallery.IsVisible = !Gallery.IsVisible;
            Controls.IsVisible = false;
            Wizard.IsVisible = !Gallery.IsVisible;
            e.Handled = true;
            return;
        }

        // F11 jumps straight to the controls screen; F10 opens the launcher
        // shell. Both are development shortcuts until the wizard's Finish hands
        // off to the launcher on its own.
        if (e.Key == Key.F11)
        {
            Controls.IsVisible = !Controls.IsVisible;
            Controls.DataContext ??= new ControlsViewModel();
            Gallery.IsVisible = false;
            LauncherShell.IsVisible = false;
            Wizard.IsVisible = !Controls.IsVisible;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F10)
        {
            LauncherShell.IsVisible = !LauncherShell.IsVisible;
            LauncherShell.DataContext ??= new LauncherViewModel();
            Gallery.IsVisible = false;
            Controls.IsVisible = false;
            Wizard.IsVisible = !LauncherShell.IsVisible;
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            Wizard.DataContext is not WizardViewModel wizard)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right:
                wizard.GoTo(wizard.CurrentIndex + 1);
                e.Handled = true;
                break;

            case Key.Left:
                wizard.GoTo(wizard.CurrentIndex - 1);
                e.Handled = true;
                break;
        }
#endif
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
