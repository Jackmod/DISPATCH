using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dispatch.Core.Detection;
using Dispatch.Core.Profiles;
using Dispatch.UI.Launcher;
using Dispatch.UI.Wizard;

namespace Dispatch.UI.Shell;

/// <summary>The application window. Code-behind is for control wiring only.</summary>
public partial class MainWindow : Window
{
    private readonly IGameBuildWatch? _buildWatch;
    private readonly IProfileStore? _profiles;
    private readonly Dispatch.Core.Platform.IGameLauncher? _launcher;

    /// <summary>Constructs the window.</summary>
    public MainWindow()
        : this(new WizardViewModel())
    {
    }

    /// <summary>Constructs the window against a composed wizard.</summary>
    public MainWindow(
        WizardViewModel wizard,
        IGameBuildWatch? buildWatch = null,
        IProfileStore? profiles = null,
        Dispatch.Core.Platform.IGameLauncher? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(wizard);

        _buildWatch = buildWatch;
        _profiles = profiles;
        _launcher = launcher;

        InitializeComponent();
        ApplyDarkTitleBar();
        Icon = TryRenderBadgeIcon();
        WireIntro();

        Wizard.DataContext = wizard;

        // Finishing the wizard drops into the launcher, which is the real
        // handoff rather than the F10 development shortcut.
        wizard.Completed += OnWizardCompleted;

        // Tunnelling, not bubbling: Avalonia's directional focus navigation
        // consumes arrow keys on the way down, so a bubbling handler never
        // sees Ctrl+Arrow â€” focus just moves instead.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Once set up, Dispatch is a launcher, not an installer: a returning user
        // drops straight into the launcher behind the intro rather than being made
        // to walk the wizard again. Runs while the intro plays, so the profile is
        // read and the swap done long before the animation fades.
        _ = TryEnterLauncherAsync();
    }

    private async Task TryEnterLauncherAsync()
    {
        if (_profiles is null)
        {
            return;
        }

        var profile = await _profiles.LoadAsync().ConfigureAwait(true);
        if (!profile.IsConfigured)
        {
            return;
        }

        LauncherShell.DataContext = new LauncherViewModel(
            profile.ActiveOfficer, _buildWatch, profile.GamePath, _launcher);
        LauncherShell.IsVisible = true;
        Wizard.IsVisible = false;
    }

    private async void OnWizardCompleted(object? sender, EventArgs e)
    {
        var officer = sender is WizardViewModel wizard
            ? await wizard.BuildOfficerAsync().ConfigureAwait(true)
            : null;

        // The game path comes from the profile the wizard just saved; it feeds the
        // build watch so the launcher can flag a game update that broke Script Hook.
        var gamePath = _profiles is not null
            ? (await _profiles.LoadAsync().ConfigureAwait(true)).GamePath
            : null;

        LauncherShell.DataContext = new LauncherViewModel(officer, _buildWatch, gamePath, _launcher);
        LauncherShell.IsVisible = true;
        Wizard.IsVisible = false;

        // Setup is done: Dispatch is now a launcher, so drop a Desktop shortcut to
        // it. Best-effort — a returning user still lands in the launcher via the
        // first-run check even if the shortcut could not be written.
        DesktopShortcut.TryCreate();
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

        // F9 opens the cleaner over a throwaway fixture folder, so the modal can
        // be seen and driven without a real game install present.
        if (e.Key == Key.F9)
        {
            LauncherShell.IsVisible = true;
            Gallery.IsVisible = false;
            Controls.IsVisible = false;
            Wizard.IsVisible = false;
            var launcher = (LauncherViewModel)(LauncherShell.DataContext ??= new LauncherViewModel());
            _ = launcher.OpenCleanerOnFixtureAsync();
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

    /// <summary>
    /// Draws the seven-point badge into a small bitmap for the window and taskbar
    /// icon, so the app carries its own mark everywhere Windows shows it.
    /// </summary>
    /// <remarks>
    /// Rendered at runtime from the same <c>ArtBadge</c> geometry the rail uses,
    /// rather than shipping a separate raster <c>.ico</c> — the identity is all
    /// vector, and one source for the badge means the icon can never drift from the
    /// mark shown inside the app. A failure here is cosmetic, so it degrades to no
    /// icon rather than taking the window down.
    /// </remarks>
    private static Avalonia.Controls.WindowIcon? TryRenderBadgeIcon()
    {
        try
        {
            if (Avalonia.Application.Current?.TryGetResource("ArtBadge", null, out var resource) != true
                || resource is not Avalonia.Media.Geometry geometry)
            {
                return null;
            }

            const int px = 64;
            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new Avalonia.PixelSize(px, px), new Avalonia.Vector(96, 96));

            using (var context = bitmap.CreateDrawingContext())
            {
                // The badge geometry lives in a 120x140 space; fit it into the
                // icon with a little breathing room and centre it.
                const double artWidth = 120, artHeight = 140;
                var scale = (px - 12) / artHeight;
                var offsetX = (px - (artWidth * scale)) / 2;
                var offsetY = (px - (artHeight * scale)) / 2;

                var gold = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8B44A"));
                var pen = new Avalonia.Media.Pen(gold, 14)
                {
                    LineJoin = Avalonia.Media.PenLineJoin.Round,
                    LineCap = Avalonia.Media.PenLineCap.Round,
                };

                using (context.PushTransform(
                    Avalonia.Matrix.CreateScale(scale, scale) * Avalonia.Matrix.CreateTranslation(offsetX, offsetY)))
                {
                    context.DrawGeometry(null, pen, geometry);
                }
            }

            return new Avalonia.Controls.WindowIcon(bitmap);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WireIntro()
    {
        // The intro plays in full every time the app opens — it is never
        // suppressed and cannot be skipped, and it runs behind the launcher swap
        // for a returning user just as it does for the wizard.
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
