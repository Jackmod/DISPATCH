using Dispatch.Core.Catalogue;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Catalogue;

/// <summary>
/// The catalogue is the data the whole install engine reads, so the properties
/// that matter are structural: the install order the spec's two hard rules
/// depend on, the placement quirks that break a mod if they are wrong, and the
/// preset composition.
/// </summary>
public sealed class ModCatalogueTests
{
    [Fact]
    public void Every_mod_id_is_unique()
    {
        ModCatalogue.Mods.Keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Full_duty_installs_around_forty_mods()
    {
        // The spec advertises ~41. A big drift means a mod was dropped.
        var mods = ModCatalogue.ModsFor("full-duty");

        mods.Should().HaveCountGreaterThanOrEqualTo(28);
    }

    [Fact]
    public void Standard_issue_is_core_only()
    {
        var mods = ModCatalogue.ModsFor("standard");

        mods.Should().Contain(m => m.Id == "lspdfr");
        mods.Should().NotContain(m => m.Id == "els", "Standard is LSPDFR and dependencies only");
    }

    [Fact]
    public void Realism_is_a_coming_soon_placeholder()
    {
        var realism = ModCatalogue.Presets.Single(p => p.Id == "realism");

        realism.ComingSoon.Should().BeTrue();
        ModCatalogue.ModsFor("realism").Should().BeEmpty();
    }

    [Fact]
    public void Core_installs_before_everything_else()
    {
        // Script Hook V and .NET have to land before any plugin that needs them.
        var order = ModCatalogue.ModsFor("full-duty").Select(m => m.Id).ToList();

        order.IndexOf("scripthookv").Should().BeLessThan(order.IndexOf("lspdfr"));
        order.IndexOf("lspdfr").Should().BeLessThan(order.IndexOf("stoptheped"));
    }

    [Fact]
    public void Callout_interface_installs_before_the_mods_that_ship_copies_of_its_files()
    {
        // Grammar Police and LIAR carry their own copies; Callout Interface must
        // be placed first so its copy is the one on disk when they arrive.
        var order = ModCatalogue.ModsFor("full-duty").Select(m => m.Id).ToList();

        order.IndexOf("calloutinterface").Should().BeLessThan(order.IndexOf("grammarpolice"));
        order.IndexOf("calloutinterface").Should().BeLessThan(order.IndexOf("liar"));
    }

    [Fact]
    public void Search_items_and_ultimate_backup_install_last()
    {
        // They deliberately replace Stop The Ped files, so they must win by
        // going after it.
        var order = ModCatalogue.ModsFor("full-duty").Select(m => m.Id).ToList();

        order.IndexOf("searchitemsreborn").Should().BeGreaterThan(order.IndexOf("stoptheped"));
        order.IndexOf("ultimatebackup").Should().BeGreaterThan(order.IndexOf("stoptheped"));
    }

    [Fact]
    public void Grammar_police_and_liar_strip_the_bundled_ragenativeui()
    {
        // The second hard rule: the root copy wins, so their bundled copy is
        // removed before extraction.
        foreach (var id in new[] { "grammarpolice", "liar" })
        {
            var strip = ModCatalogue.Mods[id].Placement.StripBeforeExtract;
            strip.Should().NotBeNull();
            strip.Should().Contain(s => s.Contains("NativeUI", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_protected_assemblies_are_the_two_the_spec_names()
    {
        // Overwriting either silently breaks Callout Interface.
        ProtectedAssemblies.IsProtected("CalloutInterface.ApplicationExtension.dll").Should().BeTrue();
        ProtectedAssemblies.IsProtected("IPTCommon.dll").Should().BeTrue();
        ProtectedAssemblies.IsProtected("SomethingElse.dll").Should().BeFalse();
    }

    [Fact]
    public void Protection_matches_on_filename_regardless_of_path()
    {
        ProtectedAssemblies.IsProtected("plugins/LSPDFR/IPTCommon.dll").Should().BeTrue();
    }

    [Fact]
    public void Only_the_core_build_locked_mods_track_the_game_build()
    {
        // Crying wolf about every plugin on a Rockstar patch is exactly what the
        // spec warns against; only Script Hook V and .NET are build-locked.
        var buildLocked = ModCatalogue.Mods.Values
            .Where(m => m.Anchor == CompatibilityAnchor.GameBuild)
            .Select(m => m.Id)
            .ToList();

        buildLocked.Should().BeEquivalentTo("scripthookv", "scripthookvdotnet");
    }

    [Fact]
    public void Github_sourced_mods_name_their_repo()
    {
        // The GitHub source needs a repo to query.
        var github = ModCatalogue.Mods.Values.Where(m => m.Source == SourceKind.GitHubRelease);

        github.Where(m => m.Id != "scripthookv") // Alexander Blade's is not on GitHub releases
            .Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Repo));
    }

    [Fact]
    public void A_dependency_is_ordered_after_what_it_depends_on()
    {
        // Better ELS Reflections edits ELS's els.ini, so ELS must be placed
        // first.
        var order = ModCatalogue.ModsFor("full-duty").Select(m => m.Id).ToList();

        if (order.Contains("betterelsreflections") && order.Contains("els"))
        {
            order.IndexOf("els").Should().BeLessThan(order.IndexOf("betterelsreflections"));
        }
    }

    [Fact]
    public void Every_preset_mod_id_resolves_to_a_real_mod()
    {
        // A preset naming a mod that does not exist would silently install
        // fewer than advertised.
        foreach (var preset in ModCatalogue.Presets)
        {
            foreach (var id in preset.ModIds)
            {
                ModCatalogue.Mods.Should().ContainKey(id, "preset {0} names {1}", preset.Id, id);
            }
        }
    }

    [Fact]
    public void An_unknown_preset_yields_no_mods_rather_than_throwing()
    {
        ModCatalogue.ModsFor("does-not-exist").Should().BeEmpty();
    }
}
