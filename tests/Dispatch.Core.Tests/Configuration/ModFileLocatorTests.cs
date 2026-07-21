using Dispatch.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The locator finds a mod's config file even when it did not land at the exact
/// relative path a catalogue predicted — the real cause of settings that silently
/// never applied. These cases are taken from actual installs: Spotlight's folder
/// is "Spotlight Resources" not "spotlight_resources", and Simple HUD and Fast Draw
/// land several folders deep inside an un-flattened archive.
/// </summary>
public sealed class ModFileLocatorTests : IDisposable
{
    private readonly string _root;

    public ModFileLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-locate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relative)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "[x]\nKey=1\n");
    }

    [Fact]
    public void The_exact_hinted_path_wins_when_it_exists()
    {
        Write("plugins/LSPDFR/CalloutInterface.ini");

        var resolved = ModFileLocator.ResolveRelative(_root, "plugins/LSPDFR/CalloutInterface.ini");

        resolved.Should().Be("plugins/LSPDFR/CalloutInterface.ini");
    }

    [Fact]
    public void A_renamed_folder_still_resolves()
    {
        // Hint says spotlight_resources; the mod ships "Spotlight Resources".
        Write("plugins/Spotlight Resources/General.ini");

        var resolved = ModFileLocator.ResolveRelative(_root, "plugins/spotlight_resources/General.ini");

        resolved.Should().Be("plugins/Spotlight Resources/General.ini");
    }

    [Fact]
    public void A_file_nested_deeper_than_the_hint_still_resolves()
    {
        // Simple HUD and Fast Draw install un-flattened, several folders deep.
        Write("scripts/SimpleHUD/Grand Theft Auto V/SimpleHUD.ini");

        var resolved = ModFileLocator.ResolveRelative(_root, "scripts/SimpleHUD.ini");

        resolved.Should().Be("scripts/SimpleHUD/Grand Theft Auto V/SimpleHUD.ini");
    }

    [Fact]
    public void A_loose_file_in_the_game_root_resolves()
    {
        Write("TrainerV.ini");

        var resolved = ModFileLocator.ResolveRelative(_root, "TrainerV.ini");

        resolved.Should().Be("TrainerV.ini");
    }

    [Fact]
    public void A_glob_hint_resolves_by_name()
    {
        Write("plugins/LSPDFR/CalloutInterface.ini");

        var resolved = ModFileLocator.ResolveRelative(_root, "plugins/LSPDFR/*CalloutInterface*.ini");

        resolved.Should().Be("plugins/LSPDFR/CalloutInterface.ini");
    }

    [Fact]
    public void An_ambiguous_name_resolves_only_by_a_matching_folder()
    {
        // Two mods each ship a "config.ini"; the hint's folder disambiguates.
        Write("plugins/LSPDFR/GrammarPolice/config.ini");
        Write("plugins/LSPDFR/OtherMod/config.ini");

        ModFileLocator.ResolveRelative(_root, "plugins/LSPDFR/GrammarPolice/config.ini")
            .Should().Be("plugins/LSPDFR/GrammarPolice/config.ini");
    }

    [Fact]
    public void An_ambiguous_name_with_no_folder_match_is_left_unresolved()
    {
        // Never guess: two like-named files and a hint folder matching neither.
        Write("plugins/ModA/config.ini");
        Write("plugins/ModB/config.ini");

        ModFileLocator.Resolve(_root, "plugins/Nowhere/config.ini").Should().BeNull();
    }

    [Fact]
    public void A_file_that_is_not_there_is_left_unresolved()
    {
        Write("plugins/Present.ini");

        ModFileLocator.Resolve(_root, "plugins/Absent.ini").Should().BeNull();
    }

    [Fact]
    public void A_backup_file_is_never_returned()
    {
        Write("plugins/LSPDFR/StopThePed.ini.bak");

        ModFileLocator.Resolve(_root, "plugins/LSPDFR/StopThePed.ini").Should().BeNull();
    }
}
