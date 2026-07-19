using Avalonia;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Theme;

/// <summary>
/// Guards the bundled type system.
///
/// Avalonia resolves an unmatched font weight to the nearest available one and
/// says nothing about it. If a weight file is dropped from Assets/Fonts, the
/// application keeps rendering — just at the wrong weight, forever, with
/// nothing in the build log. These tests assert each family/weight pair the
/// theme uses lands on the file it is supposed to.
/// </summary>
public sealed class FontResourceTests
{
    private const string FontRoot = "avares://Dispatch.UI/Assets/Fonts";

    private static IGlyphTypeface Resolve(string family, FontWeight weight)
    {
        var typeface = new Typeface(new FontFamily($"{FontRoot}#{family}"), FontStyle.Normal, weight);

        FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface)
            .Should().BeTrue($"'{family}' should be among the bundled fonts");

        return glyphTypeface!;
    }

    // The exact pairings Theme/Typography.axaml uses, style by style.
    [AvaloniaTheory]
    [InlineData("display", "Archivo Narrow", FontWeight.Bold, FontWeight.Bold)]
    [InlineData("h1", "Archivo Narrow", FontWeight.Bold, FontWeight.Bold)]
    [InlineData("h2", "Archivo Narrow", FontWeight.SemiBold, FontWeight.SemiBold)]
    [InlineData("body", "Inter", FontWeight.Normal, FontWeight.Normal)]
    [InlineData("small", "Inter", FontWeight.Medium, FontWeight.Medium)]
    [InlineData("micro", "Inter", FontWeight.Bold, FontWeight.Bold)]
    [InlineData("mono", "IBM Plex Mono", FontWeight.Normal, FontWeight.Normal)]
    public void Type_style_resolves_to_a_genuinely_drawn_weight(
        string styleName, string family, FontWeight requested, FontWeight expected)
    {
        var resolved = Resolve(family, requested);

        resolved.Weight.Should().Be(
            expected,
            $"the '{styleName}' style asks {family} for {requested}; a different weight back " +
            "means the drawn file is missing and Avalonia substituted the nearest one");
    }

    [AvaloniaTheory]
    [InlineData("Archivo Narrow")]
    [InlineData("Inter")]
    [InlineData("IBM Plex Mono")]
    public void Bundled_family_does_not_fall_back_to_a_system_font(string family)
    {
        var resolved = Resolve(family, FontWeight.Normal);

        resolved.FamilyName.Should().StartWith(
            family,
            "resolving outside the bundled family means Avalonia fell back to a system font");

        resolved.FamilyName.Should().NotBe(
            FontManager.Current.DefaultFontFamily.Name,
            "falling back to the platform default defeats the point of bundling fonts");
    }

    [AvaloniaFact]
    public void An_unbundled_family_genuinely_fails_to_resolve()
    {
        // Establishes that the assertions above can actually fail: if every
        // lookup succeeded, the tests would be vacuous.
        FontManager.Current.TryGetGlyphTypeface(
                new Typeface(new FontFamily($"{FontRoot}#Definitely Not A Real Font")), out _)
            .Should().BeFalse();
    }

    [AvaloniaFact]
    public void Display_and_body_faces_are_different_families()
    {
        // A pairing that collapses to a single face is a design regression the
        // eye catches late and this catches immediately.
        var display = Resolve("Archivo Narrow", FontWeight.Bold);
        var body = Resolve("Inter", FontWeight.Normal);

        display.FamilyName.Should().NotBe(body.FamilyName);
    }

    [AvaloniaFact]
    public void Mono_face_is_actually_monospaced()
    {
        // Paths, build numbers and key names are laid out in columns; a
        // proportional fallback here would break every alignment in the app.
        var mono = Resolve("IBM Plex Mono", FontWeight.Normal);

        var iAdvance = mono.GetGlyphAdvance(mono.GetGlyph('i'));
        var mAdvance = mono.GetGlyphAdvance(mono.GetGlyph('M'));

        iAdvance.Should().Be(mAdvance, "a monospaced face advances every glyph identically");
    }
}
