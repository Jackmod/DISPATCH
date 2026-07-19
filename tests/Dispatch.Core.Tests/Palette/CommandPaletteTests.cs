using Dispatch.Core.Palette;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Palette;

/// <summary>
/// The palette is a productivity feature, so what matters is that the obvious
/// result lands on top: typing a few letters of a thing finds that thing, and
/// finds it above coincidental matches elsewhere. A palette that ranks badly is
/// slower than the menu it replaces.
/// </summary>
public sealed class CommandPaletteTests
{
    private static readonly PaletteEntry[] Entries =
    [
        new("Dashboard", "The landing screen", "Go to", PaletteAction.Navigate, "dashboard"),
        new("Controls", "Keybindings", "Go to", PaletteAction.Navigate, "controls"),
        new("Settings", "Appearance and behaviour", "Go to", PaletteAction.Navigate, "settings"),
        new("Arrest suspect", "LSPDFR keybinding", "Keybind", PaletteAction.EditBinding, "lspdfr.arrest"),
        new("Clean GTA folder", "Remove stale mod files", "Command", PaletteAction.Run, "clean"),
        new("Pat down suspect", "Stop The Ped keybinding", "Keybind", PaletteAction.EditBinding, "stp.patdown"),
    ];

    private static IReadOnlyList<PaletteEntry> Search(string query) =>
        CommandPalette.Search(query, Entries);

    [Fact]
    public void An_empty_query_returns_everything()
    {
        Search("").Should().HaveCount(Entries.Length);
    }

    [Fact]
    public void A_direct_match_ranks_first()
    {
        Search("arrest")[0].Target.Should().Be("lspdfr.arrest");
    }

    [Fact]
    public void A_prefix_beats_a_mid_word_match()
    {
        // "set" prefixes Settings and appears mid-word nowhere that should win.
        Search("set")[0].Title.Should().Be("Settings");
    }

    [Fact]
    public void A_scattered_subsequence_still_finds_the_thing()
    {
        // Typing the consonants of a phrase is how people actually use these.
        var result = Search("clngta");

        result.Should().NotBeEmpty();
        result[0].Target.Should().Be("clean");
    }

    [Fact]
    public void Typing_a_key_action_finds_the_binding()
    {
        Search("patdown")[0].Target.Should().Be("stp.patdown");
    }

    [Fact]
    public void The_title_outweighs_the_subtitle()
    {
        // "Controls" is a section title and also nearly the word in several
        // subtitles; the section must win.
        Search("controls")[0].Target.Should().Be("controls");
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        Search("zzzzzz").Should().BeEmpty();
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        Search("DASHBOARD")[0].Target.Should().Be("dashboard");
        Search("dashboard")[0].Target.Should().Be("dashboard");
    }

    [Fact]
    public void An_incomplete_subsequence_does_not_match()
    {
        // "arrestx" has a letter the target does not, so it is not a match at
        // all rather than a weak one.
        CommandPalette.Score("arrestx", Entries[3]).Should().Be(0);
    }

    [Fact]
    public void A_subtitle_match_still_surfaces_the_entry()
    {
        // Someone types what a thing does rather than its name.
        var result = Search("appearance");

        result.Should().Contain(e => e.Target == "settings");
    }

    [Fact]
    public void The_result_count_is_capped()
    {
        var many = Enumerable.Range(0, 100)
            .Select(i => new PaletteEntry($"Item {i}", "x", "Group", PaletteAction.Run, $"item{i}"));

        CommandPalette.Search("item", many, limit: 5).Should().HaveCount(5);
    }
}
