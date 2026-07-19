using Avalonia;
using Avalonia.Headless;
using Dispatch.UI;

[assembly: AvaloniaTestApplication(typeof(Dispatch.UI.Tests.TestAppBuilder))]

namespace Dispatch.UI.Tests;

/// <summary>
/// Boots the real application object under the headless platform, so tests
/// exercise the same theme, styles and resources the shipped app uses.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Builds the headless application.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            // Headless drawing is stubbed and reports no real typefaces, which
            // would make the font tests vacuously pass. Skia gives a genuine
            // font manager against the bundled files.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
