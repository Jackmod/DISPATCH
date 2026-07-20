using Dispatch.Core.Configuration;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The settings catalogue is the data the plugin-settings screen renders and the
/// writer edits, so the promises that matter are structural: every row can be
/// found, written and shown without a gap that renders as a broken control.
/// </summary>
public sealed class SettingsCatalogueTests
{
    [Fact]
    public void Every_setting_has_the_fields_a_write_and_a_row_need()
    {
        SettingsCatalogue.Settings.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.Id) &&
            !string.IsNullOrWhiteSpace(s.Name) &&
            !string.IsNullOrWhiteSpace(s.Description) &&
            !string.IsNullOrWhiteSpace(s.Plugin) &&
            !string.IsNullOrWhiteSpace(s.ConfigFile) &&
            !string.IsNullOrWhiteSpace(s.ConfigKey));
    }

    [Fact]
    public void Setting_identifiers_are_unique()
    {
        SettingsCatalogue.Settings.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_description_reads_as_a_sentence()
    {
        // Shown under the name; a fragment looks unfinished.
        SettingsCatalogue.Settings.Should().OnlyContain(s => s.Description.EndsWith('.'));
    }

    [Fact]
    public void Toggles_have_two_distinct_literals()
    {
        var toggles = SettingsCatalogue.Settings.Where(s => s.Kind == SettingKind.Toggle);

        toggles.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.OnLiteral) &&
            !string.IsNullOrWhiteSpace(s.OffLiteral) &&
            s.OnLiteral != s.OffLiteral);
    }

    [Fact]
    public void Choices_offer_options_and_a_default_among_them()
    {
        var choices = SettingsCatalogue.Settings.Where(s => s.Kind == SettingKind.Choice);

        foreach (var choice in choices)
        {
            choice.Options.Should().NotBeEmpty("a dropdown with no options cannot be used: {0}", choice.Id);
            choice.Options.Select(o => o.Value).Should().Contain(
                choice.Default, "the default must be one of the options: {0}", choice.Id);
        }
    }

    [Fact]
    public void Numbers_have_a_sensible_range_and_an_in_range_default()
    {
        var numbers = SettingsCatalogue.Settings.Where(s => s.Kind == SettingKind.Number);

        foreach (var number in numbers)
        {
            number.Min.Should().BeLessThan(number.Max, "the range must be non-empty: {0}", number.Id);
            var def = double.Parse(number.Default, System.Globalization.CultureInfo.InvariantCulture);
            def.Should().BeInRange(number.Min, number.Max, "the default must fit the range: {0}", number.Id);
        }
    }

    [Fact]
    public void Profile_backed_settings_resolve_the_officers_value()
    {
        var officer = new OfficerProfile
        {
            Id = Guid.NewGuid(),
            Name = "R. Vance",
            Agency = Agency.Bcso,
            CallsignDivision = 2,
            CallsignPhonetic = "LINCOLN",
            CallsignBeat = 14,
            DepartmentName = "Blaine County Sheriff",
        };

        var callsign = SettingsCatalogue.Settings.First(s => s.Profile == ProfileField.Callsign);
        callsign.DefaultFor(officer).Should().Be("2 LINCOLN 14");

        var agencyBacked = SettingsCatalogue.Settings.FirstOrDefault(s => s.Profile == ProfileField.AgencyCode);
        agencyBacked?.DefaultFor(officer).Should().BeOneOf("BCSO", null);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData("On", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("NO", false)]
    [InlineData("", false)]
    public void ParseBool_is_tolerant_of_every_truthy_spelling(string raw, bool expected)
    {
        var setting = SettingsCatalogue.Settings.First(s => s.Kind == SettingKind.Toggle);

        setting.ParseBool(raw).Should().Be(expected);
    }

    [Fact]
    public void The_plugin_list_is_populated_for_the_filter()
    {
        SettingsCatalogue.Plugins.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
    }
}
