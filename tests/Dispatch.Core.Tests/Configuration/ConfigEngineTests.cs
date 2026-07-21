using Dispatch.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The config engine: matching the guide's human setting names to a file's real
/// keys, filling officer values, and never corrupting a file it does not fully
/// understand.
/// </summary>
public sealed class ConfigEngineTests
{
    private static IniConfigWriter Writer() => new(NullLogger<IniConfigWriter>.Instance);

    [Fact]
    public void A_setting_matches_a_key_despite_different_spacing_and_case()
    {
        var doc = IniDocument.Parse(
            "[Keys]\n" +
            "OpenComputerKey = F5   ; hold to open\n" +
            "GiveCitationKey = C\n");

        var result = Writer().Apply(doc,
            [new ConfigSetting("Open Computer Key", "X")], OfficerValues.Default);

        result.Changed.Should().BeTrue();
        doc.Get("Keys", "OpenComputerKey").Should().Be("X");
        result.Unmatched.Should().BeEmpty();
    }

    [Fact]
    public void The_comment_and_other_lines_are_left_untouched()
    {
        var doc = IniDocument.Parse(
            "; my hand-tuned file\n" +
            "[Keys]\n" +
            "OpenComputerKey = F5   ; hold to open\n");

        Writer().Apply(doc, [new ConfigSetting("Open Computer Key", "X")], OfficerValues.Default);

        var text = doc.ToText();
        text.Should().Contain("; my hand-tuned file");
        text.Should().Contain("; hold to open", "the inline comment survives");
        text.Should().Contain("OpenComputerKey = X");
    }

    [Fact]
    public void Officer_placeholders_are_filled()
    {
        var doc = IniDocument.Parse("MDTCallSign = OLD\nUnitName = OLD\n");
        var officer = new OfficerValues("3 LINCOLN 22", "Jack Portman", "Los Santos PD", "AIR 2");

        Writer().Apply(doc,
        [
            new ConfigSetting("MDT Call Sign", "{callsign}"),
            new ConfigSetting("Unit Name", "{officer}"),
        ], officer);

        doc.Get("", "MDTCallSign").Should().Be("3 LINCOLN 22");
        doc.Get("", "UnitName").Should().Be("Jack Portman");
    }

    [Fact]
    public void A_contains_match_writes_every_matching_key()
    {
        // Grammar Police: "Every Use Natives flag = False".
        var doc = IniDocument.Parse(
            "UseNativesForBlips = True\n" +
            "UseNativesForRadio = True\n" +
            "SomethingElse = True\n");

        Writer().Apply(doc,
            [new ConfigSetting("Use Natives", "False", ConfigMatch.Contains)], OfficerValues.Default);

        doc.Get("", "UseNativesForBlips").Should().Be("False");
        doc.Get("", "UseNativesForRadio").Should().Be("False");
        doc.Get("", "SomethingElse").Should().Be("True", "it does not contain the token");
    }

    [Fact]
    public void An_unmatched_setting_is_reported_and_the_file_is_not_touched()
    {
        var doc = IniDocument.Parse("SomeKey = 1\n");
        var before = doc.ToText();

        var result = Writer().Apply(doc,
            [new ConfigSetting("A Setting That Does Not Exist", "value")], OfficerValues.Default);

        result.Changed.Should().BeFalse();
        result.Unmatched.Should().ContainSingle().Which.Should().Be("A Setting That Does Not Exist");
        doc.ToText().Should().Be(before, "nothing is invented for a key that is not there");
    }

    [Fact]
    public void Normalise_strips_case_spaces_and_punctuation()
    {
        IniConfigWriter.Normalise("Open Computer Key").Should().Be("opencomputerkey");
        IniConfigWriter.Normalise("MDT_Call-Sign").Should().Be("mdtcallsign");
    }

    // ===== The catalogue itself ==========================================

    [Fact]
    public void The_catalogue_keybinds_do_not_clash()
    {
        var clashes = KeybindClashDetector.Detect(ConfigCatalogue.All);

        clashes.Should().BeEmpty(
            "the guide's values are chosen so nothing collides — any clash here is a catalogue error: {0}",
            string.Join(" | ", clashes.Select(c => $"{c.Binding} = [{string.Join(", ", c.Actions)}]")));
    }

    [Fact]
    public void Every_catalogue_config_maps_to_a_known_mod_id()
    {
        // Config entries must line up with catalogue mod ids or the install pass
        // filters them out and they never run. A mod may appear more than once when
        // it keeps its config in more than one file (LSPDFR: keys.ini + lspdfr.ini),
        // so duplicates are expected — every id just has to be a real mod.
        var known = Dispatch.Core.Catalogue.ModCatalogue.Mods.Keys.ToHashSet(StringComparer.Ordinal);

        var unknown = ConfigCatalogue.All
            .Select(c => c.ModId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !known.Contains(id))
            .ToList();

        unknown.Should().BeEmpty("every config mod id should be a real mod: {0}", string.Join(", ", unknown));
    }
}
