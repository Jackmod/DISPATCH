using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The archive matcher: turning a folder of dumped, oddly-named archives into a
/// mod-to-file map. These pin the behaviour a user relies on — that real
/// download names land on the right mod, and an unrecognisable file is left
/// unmatched rather than forced onto the wrong one.
/// </summary>
public sealed class ModArchiveMatcherTests
{
    private static IReadOnlyList<ModDefinition> Catalogue => ModCatalogue.Mods.Values.ToList();

    private static string ModFor(ModArchiveMatcher.Result result, string archiveName) =>
        result.Matches.Values.FirstOrDefault(m =>
            Path.GetFileName(m.ArchivePath).Equals(archiveName, StringComparison.OrdinalIgnoreCase))?.ModId ?? "(none)";

    [Theory]
    [InlineData("ELS V1.05.rar", "els")]
    [InlineData("StopThePed.zip", "stoptheped")]
    [InlineData("CalloutInterface.zip", "calloutinterface")]
    [InlineData("Ultimate Backup 1.5.rar", "ultimatebackup")]
    [InlineData("ScriptHookV_1.0.3570.1.zip", "scripthookv")]
    [InlineData("LSPDFR-0.4.9-RELEASE.zip", "lspdfr")]
    [InlineData("ClearTheWayV.zip", "clearthewayv")]
    [InlineData("Radio Realism.rar", "radiorealism")]
    public void Real_download_names_land_on_the_right_mod(string fileName, string expectedModId)
    {
        var result = ModArchiveMatcher.Match([Path.Combine("pack", fileName)], Catalogue);

        ModFor(result, fileName).Should().Be(expectedModId);
    }

    [Theory]
    [InlineData("TrainerV.zip", "simpletrainer")]   // ships as TrainerV, alias
    [InlineData("Charges.zip", "charges")]          // Charges & Citations (split from the Compulite plugin)
    [InlineData("Heli Assist.rar", "heliassistance")]
    public void Aliases_cover_names_that_look_nothing_like_the_mod(string fileName, string expectedModId)
    {
        var result = ModArchiveMatcher.Match([Path.Combine("pack", fileName)], Catalogue);

        ModFor(result, fileName).Should().Be(expectedModId);
    }

    [Fact]
    public void An_unrecognisable_archive_is_left_unmatched()
    {
        var result = ModArchiveMatcher.Match(["pack/some-random-texture-pack.rar"], Catalogue);

        result.Matches.Should().BeEmpty();
        result.UnmatchedArchives.Should().ContainSingle();
    }

    [Fact]
    public void Each_archive_and_each_mod_is_used_at_most_once()
    {
        // Two archives that could both weakly hit ELS; the better match keeps ELS,
        // the other must not also be assigned to it.
        var result = ModArchiveMatcher.Match(
            ["pack/ELS V1.05.rar", "pack/Better ELS Reflections.rar"], Catalogue);

        ModFor(result, "ELS V1.05.rar").Should().Be("els");
        ModFor(result, "Better ELS Reflections.rar").Should().Be("betterelsreflections");
    }

    [Fact]
    public void A_more_specific_name_wins_over_a_shorter_one()
    {
        // "Ultimate Backup" must not be stolen by a mod with a short generic key.
        var result = ModArchiveMatcher.Match(["pack/Ultimate Backup.rar"], Catalogue);

        ModFor(result, "Ultimate Backup.rar").Should().Be("ultimatebackup");
    }

    [Fact]
    public void Reduce_strips_versions_boilerplate_and_separators()
    {
        ModArchiveMatcher.Reduce("ELS V1.05").Should().Be("els");
        // Short glue words are kept, so a spaced name and a glued id reduce alike.
        ModArchiveMatcher.Reduce("Stop_The_Ped").Should().Be("stoptheped");
        ModArchiveMatcher.Reduce("RAGENativeUI mod release").Should().Be("ragenativeui");
    }
}
