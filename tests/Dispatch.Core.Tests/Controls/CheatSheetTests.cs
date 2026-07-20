using Dispatch.Core.Controls;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Controls;

/// <summary>
/// The cheat sheet is generated from the same catalogue the screen renders, so it
/// can never claim a key the app is not actually bound to. These assert the shape
/// a printed reference needs: the officer's identity, both devices, real actions
/// in plain words, and nothing listed that is unbound.
/// </summary>
public sealed class CheatSheetTests
{
    private static readonly OfficerProfile Officer = new()
    {
        Id = Guid.NewGuid(),
        Name = "J. Doe",
        Agency = Agency.Lspd,
        CallsignDivision = 1,
        CallsignPhonetic = "ADAM",
        CallsignBeat = 7,
    };

    [Fact]
    public void The_sheet_leads_with_the_officer_and_covers_both_devices()
    {
        var sheet = CheatSheet.BuildMarkdown(ControlCatalogue.Bind(ControlCatalogue.Suggested), Officer);

        sheet.Should().Contain("Dispatch — Control Cheat Sheet");
        sheet.Should().Contain("1 ADAM 7").And.Contain("J. Doe").And.Contain("LSPD");
        sheet.Should().Contain("## Keyboard").And.Contain("## Controller");
    }

    [Fact]
    public void Actions_appear_in_plain_words_with_their_keys()
    {
        var sheet = CheatSheet.BuildMarkdown(ControlCatalogue.Bind(ControlCatalogue.Suggested), Officer);

        // A real action and the de-facto-standard key it is bound to.
        sheet.Should().Contain("Start traffic stop");
        sheet.Should().Contain("Left Shift");
        // Never a raw config token.
        sheet.Should().NotContain("LShiftKey");
    }

    [Fact]
    public void Unbound_actions_are_left_out()
    {
        // A reference is for what you can do, not a census of what you cannot.
        var scheme = new Dictionary<string, KeyBinding>
        {
            ["lspdfr.arrest"] = new(KeyTokens.Parse("E")),
        };

        var sheet = CheatSheet.BuildMarkdown(ControlCatalogue.Bind(scheme));

        sheet.Should().Contain("Arrest suspect");
        sheet.Should().NotContain("Unbound");
    }

    [Fact]
    public void An_empty_scheme_produces_a_header_but_no_device_sections()
    {
        var sheet = CheatSheet.BuildMarkdown(ControlCatalogue.Bind(new Dictionary<string, KeyBinding>()));

        sheet.Should().Contain("Dispatch — Control Cheat Sheet");
        sheet.Should().NotContain("## Keyboard");
        sheet.Should().NotContain("## Controller");
    }

    [Fact]
    public void The_plain_text_form_drops_the_markdown_emphasis()
    {
        var text = CheatSheet.BuildPlainText(ControlCatalogue.Bind(ControlCatalogue.Suggested), Officer);

        text.Should().Contain("1 ADAM 7");
        text.Should().NotContain("**");
    }
}
