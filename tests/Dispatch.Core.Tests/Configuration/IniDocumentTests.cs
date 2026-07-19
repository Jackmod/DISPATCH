using System.Text;
using Dispatch.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The ini model edits values in place and disturbs nothing else. These files
/// carry comments explaining each setting, a deliberate key order, and keys a
/// mod update added that this build has never heard of. Losing any of that to
/// a keybind change is exactly the silent corruption the app exists to prevent,
/// so the round-trip guarantees are asserted byte for byte.
/// </summary>
public sealed class IniDocumentTests
{
    private const string Sample =
        "; Stop The Ped configuration\n" +
        "; Do not edit while the game is running.\n" +
        "\n" +
        "[General]\n" +
        "PatDownKey = F9   ; the pat-down key\n" +
        "TransportKey=9\n" +
        "RealisticWeapons = NO\n" +
        "\n" +
        "[Transport]\n" +
        "NearestCop = YES\n";

    [Fact]
    public void An_untouched_document_round_trips_byte_for_byte()
    {
        var document = IniDocument.Parse(Sample);

        document.ToText().Should().Be(Sample);
    }

    [Fact]
    public void Changing_a_value_leaves_every_other_line_identical()
    {
        var document = IniDocument.Parse(Sample);

        document.Set("General", "TransportKey", "D9");

        document.ToText().Should().Be(Sample.Replace("TransportKey=9", "TransportKey=D9"));
    }

    [Fact]
    public void Changing_a_value_preserves_the_inline_comment()
    {
        // The comment explains what the key does. A rebind that ate it would
        // leave the next person editing by hand with no idea what the line was.
        var document = IniDocument.Parse(Sample);

        document.Set("General", "PatDownKey", "F10");

        document.ToText().Should().Contain("PatDownKey = F10   ; the pat-down key");
    }

    [Fact]
    public void The_original_spacing_around_the_separator_is_kept()
    {
        // One key uses "= " and another "=". Both survive unchanged.
        var document = IniDocument.Parse(Sample);

        document.Set("General", "PatDownKey", "X");
        document.Set("General", "TransportKey", "Y");

        document.ToText().Should().Contain("PatDownKey = X").And.Contain("TransportKey=Y");
    }

    [Fact]
    public void Comments_and_blank_lines_are_never_dropped()
    {
        var document = IniDocument.Parse(Sample);

        document.Set("General", "RealisticWeapons", "YES");

        var text = document.ToText();
        text.Should().Contain("; Stop The Ped configuration");
        text.Should().Contain("; Do not edit while the game is running.");
        text.Split('\n').Count(string.IsNullOrEmpty).Should().Be(Sample.Split('\n').Count(string.IsNullOrEmpty));
    }

    [Fact]
    public void Reading_a_value_ignores_surrounding_whitespace()
    {
        var document = IniDocument.Parse(Sample);

        document.Get("General", "PatDownKey").Should().Be("F9");
        document.Get("Transport", "NearestCop").Should().Be("YES");
    }

    [Fact]
    public void Section_and_key_lookups_are_case_insensitive()
    {
        // These files disagree about casing, and a person does not expect
        // CallSign and Callsign to be different settings.
        var document = IniDocument.Parse(Sample);

        document.Get("general", "patdownkey").Should().Be("F9");
        document.Set("GENERAL", "PATDOWNKEY", "X").Should().BeTrue();
        document.Get("General", "PatDownKey").Should().Be("X");
    }

    [Fact]
    public void Setting_a_value_to_what_it_already_is_changes_nothing()
    {
        // The staged-changes diff must never list a line that did not move.
        var document = IniDocument.Parse(Sample);

        document.Set("General", "PatDownKey", "F9").Should().BeFalse();
        document.ToText().Should().Be(Sample);
    }

    [Fact]
    public void A_missing_key_is_added_to_its_section()
    {
        // A mod update may add a key the profile does not yet carry. The write
        // has to create it rather than fail.
        var document = IniDocument.Parse(Sample);

        document.Set("General", "NewKey", "1").Should().BeTrue();

        document.Get("General", "NewKey").Should().Be("1");
        // Added inside [General], not appended after [Transport].
        var lines = document.ToText().Split('\n');
        var generalAt = Array.FindIndex(lines, l => l.Contains("[General]"));
        var transportAt = Array.FindIndex(lines, l => l.Contains("[Transport]"));
        var newKeyAt = Array.FindIndex(lines, l => l.Contains("NewKey"));
        newKeyAt.Should().BeGreaterThan(generalAt).And.BeLessThan(transportAt);
    }

    [Fact]
    public void A_missing_section_is_created()
    {
        var document = IniDocument.Parse(Sample);

        document.Set("Brand New", "Key", "value");

        document.Sections.Should().Contain("Brand New");
        document.Get("Brand New", "Key").Should().Be("value");
    }

    [Fact]
    public void CRLF_line_endings_are_preserved()
    {
        // Never rewrite line endings as a side effect; a diff tool would flag
        // every line as changed.
        var crlf = "[A]\r\nKey = 1\r\nOther = 2\r\n";
        var document = IniDocument.Parse(crlf);

        document.Set("A", "Key", "9");

        document.ToText().Should().Be("[A]\r\nKey = 9\r\nOther = 2\r\n");
    }

    [Fact]
    public void A_file_without_a_trailing_newline_keeps_it_that_way()
    {
        var noTrailing = "[A]\nKey = 1";
        var document = IniDocument.Parse(noTrailing);

        document.Set("A", "Key", "2");

        document.ToText().Should().Be("[A]\nKey = 2");
    }

    [Fact]
    public void Root_keys_before_any_section_are_read_and_written()
    {
        // Some mod files start with keys and no header at all.
        var rootFirst = "Global = on\n\n[Section]\nKey = 1\n";
        var document = IniDocument.Parse(rootFirst);

        document.Get(string.Empty, "Global").Should().Be("on");
        document.Set(string.Empty, "Global", "off");
        document.ToText().Should().StartWith("Global = off");
    }

    [Fact]
    public void Duplicate_keys_across_sections_do_not_cross_contaminate()
    {
        // Grammar Police duplicates [default] to [custom]; both hold a CallSign.
        var duplicated = "[default]\nCallSign = 1 ADAM 7\n\n[custom]\nCallSign = 1 ADAM 7\n";
        var document = IniDocument.Parse(duplicated);

        document.Set("custom", "CallSign", "2 LINCOLN 14");

        document.Get("default", "CallSign").Should().Be("1 ADAM 7");
        document.Get("custom", "CallSign").Should().Be("2 LINCOLN 14");
    }

    [Fact]
    public async Task A_utf8_bom_survives_a_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dispatch-ini-{Guid.NewGuid():N}.ini");
        try
        {
            var withBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            await File.WriteAllTextAsync(path, "[A]\nKey = 1\n", withBom);

            var document = await IniDocument.LoadAsync(path);
            document.Set("A", "Key", "2");
            await document.SaveAsync(path);

            var bytes = await File.ReadAllBytesAsync(path);
            bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }, "the BOM must survive");
            (await File.ReadAllTextAsync(path)).Should().Contain("Key = 2");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_utf16_file_is_not_silently_converted_to_utf8()
    {
        // Never change a file's encoding as a side effect of editing it.
        var path = Path.Combine(Path.GetTempPath(), $"dispatch-ini-{Guid.NewGuid():N}.ini");
        try
        {
            await File.WriteAllTextAsync(path, "[A]\nKey = 1\n", new UnicodeEncoding(false, true));

            var document = await IniDocument.LoadAsync(path);
            document.Set("A", "Key", "2");
            await document.SaveAsync(path);

            var bytes = await File.ReadAllBytesAsync(path);
            bytes.Take(2).Should().Equal(new byte[] { 0xFF, 0xFE }, "the UTF-16 LE BOM must survive");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Non_ascii_values_are_preserved()
    {
        var document = IniDocument.Parse("[A]\nName = Café Ünïcode\n");

        document.Get("A", "Name").Should().Be("Café Ünïcode");
        document.Set("A", "Name", "Zürich");
        document.Get("A", "Name").Should().Be("Zürich");
    }

    [Fact]
    public void Keys_in_a_section_are_listed_in_file_order()
    {
        var document = IniDocument.Parse(Sample);

        document.KeysIn("General").Should().Equal("PatDownKey", "TransportKey", "RealisticWeapons");
    }

    [Fact]
    public void An_absent_key_reads_as_null_rather_than_throwing()
    {
        var document = IniDocument.Parse(Sample);

        document.Get("General", "Nope").Should().BeNull();
        document.Has("General", "Nope").Should().BeFalse();
    }
}
