using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Theme;

/// <summary>
/// Guards the design tokens themselves: that every key the application binds to
/// exists, and that the reduced-motion swap genuinely collapses durations
/// rather than merely being wired up.
/// </summary>
public sealed class ThemeResourceTests
{
    private static object Resource(string key)
    {
        Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var value)
            .Should().BeTrue($"'{key}' should be defined by the theme");
        return value!;
    }

    [AvaloniaTheory]
    [InlineData("Ink")]
    [InlineData("NavyDeep")]
    [InlineData("Navy")]
    [InlineData("NavyRaised")]
    [InlineData("Steel")]
    [InlineData("SteelLight")]
    [InlineData("Blue")]
    [InlineData("BlueBright")]
    [InlineData("BlueDim")]
    [InlineData("Gold")]
    [InlineData("GoldBright")]
    [InlineData("GoldDim")]
    [InlineData("Green")]
    [InlineData("Amber")]
    [InlineData("Red")]
    [InlineData("Text")]
    [InlineData("TextMuted")]
    [InlineData("TextFaint")]
    public void Every_palette_entry_is_a_brush(string key) =>
        Resource(key).Should().BeAssignableTo<IBrush>();

    [AvaloniaTheory]
    [InlineData("Space4", 4d)]
    [InlineData("Space8", 8d)]
    [InlineData("Space12", 12d)]
    [InlineData("Space16", 16d)]
    [InlineData("Space24", 24d)]
    [InlineData("Space32", 32d)]
    [InlineData("Space48", 48d)]
    [InlineData("Space64", 64d)]
    public void Spacing_scale_is_the_4px_series(string key, double expected) =>
        Resource(key).Should().Be(expected);

    [AvaloniaTheory]
    [InlineData("RadiusInput", 6d)]
    [InlineData("RadiusCard", 10d)]
    [InlineData("RadiusModal", 14d)]
    [InlineData("RadiusPill", 999d)]
    public void Corner_radii_are_the_four_shape_tokens(string key, double expected) =>
        Resource(key).Should().Be(new CornerRadius(expected));

    [AvaloniaTheory]
    [InlineData("MicroDuration", 120)]
    [InlineData("StandardDuration", 220)]
    [InlineData("PageDuration", 380)]
    [InlineData("StaggerStep", 40)]
    public void Motion_durations_match_the_timing_system(string key, int milliseconds) =>
        Resource(key).Should().Be(TimeSpan.FromMilliseconds(milliseconds));

    [AvaloniaFact]
    public void Nothing_animates_longer_than_400ms_except_the_intro()
    {
        string[] everyday = ["MicroDuration", "StandardDuration", "PageDuration", "StaggerStep"];

        foreach (var key in everyday)
        {
            ((TimeSpan)Resource(key)).Should().BeLessThanOrEqualTo(
                TimeSpan.FromMilliseconds(400),
                $"'{key}' is an everyday transition");
        }

        ((TimeSpan)Resource("IntroDuration")).Should().Be(TimeSpan.FromMilliseconds(1400));
    }

    [AvaloniaFact]
    public void Reduced_motion_collapses_every_duration_to_zero()
    {
        var app = (App)Application.Current!;

        try
        {
            app.SetReducedMotion(true);

            string[] durations =
            [
                "MicroDuration",
                "StandardDuration",
                "PageDuration",
                "StaggerStep",
                "IntroDuration",
            ];

            foreach (var key in durations)
            {
                ((TimeSpan)Resource(key)).Should().Be(
                    TimeSpan.Zero,
                    $"'{key}' must be zero when reduced motion is on");
            }
        }
        finally
        {
            app.SetReducedMotion(false);
        }
    }

    [AvaloniaFact]
    public void Turning_reduced_motion_back_off_restores_the_timings()
    {
        var app = (App)Application.Current!;

        app.SetReducedMotion(true);
        app.SetReducedMotion(false);

        ((TimeSpan)Resource("StandardDuration")).Should().Be(TimeSpan.FromMilliseconds(220));
        ((TimeSpan)Resource("IntroDuration")).Should().Be(TimeSpan.FromMilliseconds(1400));
    }

    [AvaloniaFact]
    public void Body_text_meets_4_5_to_1_contrast_against_the_card_surface()
    {
        // Accessibility is specced as non-negotiable rather than a later pass,
        // so the palette itself is asserted rather than trusted.
        var text = ((ISolidColorBrush)Resource("Text")).Color;
        var navy = ((ISolidColorBrush)Resource("Navy")).Color;

        ContrastRatio(text, navy).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [AvaloniaFact]
    public void Muted_text_still_meets_4_5_to_1_against_the_card_surface()
    {
        var muted = ((ISolidColorBrush)Resource("TextMuted")).Color;
        var navy = ((ISolidColorBrush)Resource("Navy")).Color;

        ContrastRatio(muted, navy).Should().BeGreaterThanOrEqualTo(4.5);
    }

    /// <summary>WCAG 2.1 relative luminance contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (lighter, darker) = la > lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte raw)
        {
            var v = raw / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }
}
