using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Installation;
using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Installation;

/// <summary>
/// The three placement behaviours added for real archives: extract only from a
/// mod's SourceFolder subtree, never carry documentation into the game folder,
/// and set OpenIV-bound files aside instead of placing them.
/// </summary>
public sealed class PlacementRoutingTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-routing", Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly string _staging;
    private readonly AppPaths _paths;
    private readonly LocalInstallRunner _runner;

    public PlacementRoutingTests()
    {
        _game = Path.Combine(_root, "game");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_staging);

        _paths = new AppPaths(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        var placer = new FilePlacer(
            new BackupStore(_paths.BackupsDirectory, NullLogger<BackupStore>.Instance),
            NullLogger<FilePlacer>.Instance);
        _runner = new LocalInstallRunner(placer, _paths, NullLogger<LocalInstallRunner>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private LocalInstallRunner.StagedMod Stage(string modId, params (string Path, string Content)[] files)
    {
        var mod = ModCatalogue.Mods[modId];
        var folder = Path.Combine(_staging, modId);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return new LocalInstallRunner.StagedMod(mod, folder);
    }

    private bool InGame(string relative) =>
        File.Exists(Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar)));

    // ===== SourceFolder ===================================================

    [Fact]
    public async Task Only_the_source_folder_subtree_is_placed_even_under_a_wrapper()
    {
        // ELS installs from "Installation Files/Grand Theft Auto V", nested under
        // a wrapper folder as real archives are. Only that subtree lands, mapped
        // to the game root — the wrapper and the navigation folders are dropped.
        var els = Stage("els",
            ("ELS v1.05/Installation Files/Grand Theft Auto V/ELS.asi", "asi"),
            ("ELS v1.05/Installation Files/Grand Theft Auto V/plugins/ELS.dll", "dll"),
            ("ELS v1.05/Documentation/how it works.pdf", "doc"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [els]);

        InGame("ELS.asi").Should().BeTrue("it is directly under the source folder");
        InGame("plugins/ELS.dll").Should().BeTrue("its subfolder structure is preserved");
        InGame("Installation Files/Grand Theft Auto V/ELS.asi").Should().BeFalse("the navigation path is stripped");
        Directory.Exists(Path.Combine(_game, "Documentation")).Should().BeFalse("outside the source folder");
    }

    [Fact]
    public async Task A_plugins_folder_mod_lands_in_the_game_plugins_folder()
    {
        var stp = Stage("stoptheped", ("plugins/StopThePed.dll", "code"), ("plugins/StopThePed.ini", "cfg"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [stp]);

        InGame("plugins/StopThePed.dll").Should().BeTrue();
        InGame("plugins/StopThePed.ini").Should().BeTrue();
        InGame("StopThePed.dll").Should().BeFalse("it belongs in plugins, not the root");
    }

    // ===== Guide-audited real layouts ====================================

    [Fact]
    public async Task Callout_interface_places_its_shared_dlls_data_tree_and_plugin()
    {
        // It ships far more than CalloutInterface.dll: the shared DLLs at its GTA V
        // root, its plugin under plugins/LSPDFR, and its whole data tree. All of it
        // must land, and the bundled RAGENativeUI must be dropped.
        var ci = Stage("calloutinterface",
            ("CalloutInterface-1.4.1/Grand Theft Auto V/CalloutInterfaceAPI.dll", "api"),
            ("CalloutInterface-1.4.1/Grand Theft Auto V/IPT.Common.dll", "ipt"),
            ("CalloutInterface-1.4.1/Grand Theft Auto V/RawCanvasUI.dll", "canvas"),
            ("CalloutInterface-1.4.1/Grand Theft Auto V/RAGENativeUI.dll", "stale"),
            ("CalloutInterface-1.4.1/Grand Theft Auto V/plugins/LSPDFR/CalloutInterface.dll", "plugin"),
            ("CalloutInterface-1.4.1/Grand Theft Auto V/plugins/LSPDFR/CalloutInterface/alpr.xml", "data"),
            ("CalloutInterface-1.4.1/README.html", "doc"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [ci]);

        InGame("CalloutInterfaceAPI.dll").Should().BeTrue();
        InGame("IPT.Common.dll").Should().BeTrue();
        InGame("RawCanvasUI.dll").Should().BeTrue();
        InGame("plugins/LSPDFR/CalloutInterface.dll").Should().BeTrue();
        InGame("plugins/LSPDFR/CalloutInterface/alpr.xml").Should().BeTrue("its data tree comes too");
        InGame("RAGENativeUI.dll").Should().BeFalse("the bundled copy is stripped");
        InGame("README.html").Should().BeFalse();
    }

    [Fact]
    public async Task A_later_mod_cannot_overwrite_callout_interfaces_shared_dlls()
    {
        // Callout Interface (order 10) installs first; Grammar Police (order 40) ships
        // its own CalloutInterfaceAPI.dll / IPT.Common.dll, which must NOT overwrite
        // the copies Callout Interface placed — the protected-assemblies guard, on the
        // current file names.
        var ci = Stage("calloutinterface",
            ("CI/Grand Theft Auto V/CalloutInterfaceAPI.dll", "callout-copy"),
            ("CI/Grand Theft Auto V/IPT.Common.dll", "callout-copy"),
            ("CI/Grand Theft Auto V/plugins/LSPDFR/CalloutInterface.dll", "ci"));
        var gp = Stage("grammarpolice",
            ("GP/Grand Theft Auto V/CalloutInterfaceAPI.dll", "grammar-copy"),
            ("GP/Grand Theft Auto V/IPT.Common.dll", "grammar-copy"),
            ("GP/Grand Theft Auto V/plugins/LSPDFR/GrammarPolice.dll", "gp"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [ci, gp]);

        File.ReadAllText(Path.Combine(_game, "CalloutInterfaceAPI.dll")).Should().Be("callout-copy",
            "Grammar Police must not overwrite Callout Interface's shared dll");
        File.ReadAllText(Path.Combine(_game, "IPT.Common.dll")).Should().Be("callout-copy");
        InGame("plugins/LSPDFR/GrammarPolice.dll").Should().BeTrue("its own plugin still installs");
    }

    [Fact]
    public async Task Fast_draw_scripts_land_in_the_game_scripts_folder_not_nested()
    {
        var fd = Stage("fastdraw",
            ("Fast Draw v1.2/GTAV/scripts/Fast_Draw.dll", "code"),
            ("Fast Draw v1.2/GTAV/scripts/Fast_Draw_Settings.ini", "cfg"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [fd]);

        InGame("scripts/Fast_Draw.dll").Should().BeTrue();
        InGame("scripts/Fast_Draw_Settings.ini").Should().BeTrue();
        InGame("scripts/Fast Draw v1.2/GTAV/scripts/Fast_Draw.dll").Should().BeFalse("the wrapper must not nest");
    }

    [Fact]
    public async Task Simple_hud_asi_goes_to_the_game_root_not_scripts()
    {
        var hud = Stage("simplehud",
            ("SimpleHUD/Grand Theft Auto V/SimpleHUD.asi", "asi"),
            ("SimpleHUD/Grand Theft Auto V/SimpleHUD.ini", "cfg"),
            ("SimpleHUD/Documentation/readme.txt", "doc"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [hud]);

        InGame("SimpleHUD.asi").Should().BeTrue("it's an asi loaded from the game root");
        InGame("SimpleHUD.ini").Should().BeTrue();
        InGame("scripts/SimpleHUD.asi").Should().BeFalse("this version is not a scripts mod");
    }

    [Fact]
    public async Task Radio_realism_scanner_audio_keeps_its_resident_subfolder()
    {
        var rr = Stage("radiorealism",
            ("RadioRealismAlphaV1.2/Grand Theft Auto V/LSPDFR/Audio/scanner/RESIDENT/INSERT_01.wav", "audio"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [rr]);

        InGame("lspdfr/audio/scanner/RESIDENT/INSERT_01.wav").Should().BeTrue("LSPDFR reads it from scanner/RESIDENT");
        InGame("lspdfr/audio/scanner/INSERT_01.wav").Should().BeFalse("it must not be flattened into scanner");
    }

    // ===== AutoDetect =====================================================

    [Fact]
    public async Task AutoDetect_strips_the_wrapper_and_merges_game_folders()
    {
        // A typical LSPDFR plugin archive: a wrapper folder holding plugins and
        // scripts. AutoDetect strips the wrapper and merges both into the game.
        var mod = Stage("stickywheels",
            ("StickyWheels v0.2/plugins/StickyWheels.dll", "code"),
            ("StickyWheels v0.2/plugins/lspdfr/config.xml", "cfg"),
            ("StickyWheels v0.2/scripts/helper.dll", "script"),
            ("StickyWheels v0.2/readme.txt", "docs"));

        await _runner.RunAsync("run", _game, "realism", "1.0.3725", [mod]);

        InGame("plugins/StickyWheels.dll").Should().BeTrue("the plugins folder merges into the game");
        InGame("plugins/lspdfr/config.xml").Should().BeTrue("its substructure is preserved");
        InGame("scripts/helper.dll").Should().BeTrue("the scripts folder merges too");
        InGame("readme.txt").Should().BeFalse("the wrapper's readme is still junk");
        Directory.Exists(Path.Combine(_game, "StickyWheels v0.2")).Should().BeFalse("the wrapper is stripped");
    }

    [Fact]
    public async Task AutoDetect_stops_stripping_at_a_game_folder()
    {
        // When the wrapper's only child IS a game folder (plugins), it must be
        // kept, not descended into — otherwise the files land at the root.
        var mod = Stage("kucheracallouts", ("plugins/Kuchera.dll", "code"));

        await _runner.RunAsync("run", _game, "realism", "1.0.3725", [mod]);

        InGame("plugins/Kuchera.dll").Should().BeTrue();
        InGame("Kuchera.dll").Should().BeFalse("plugins is kept intact, not unwrapped");
    }

    // ===== Junk exclusion =================================================

    [Fact]
    public async Task Readmes_licenses_and_shortcuts_never_reach_the_game_folder()
    {
        var mod = Stage("stoptheped",
            ("plugins/StopThePed.dll", "code"),
            ("plugins/readme.txt", "how to install"),
            ("plugins/LICENSE.md", "license text"),
            ("plugins/Visit our site.url", "[InternetShortcut]"),
            ("plugins/changelog.txt", "v1 v2 v3"),
            ("plugins/manual.pdf", "%PDF"),          // a pure-doc format, always junk
            ("plugins/credits.rtf", "thanks"),       // another doc format
            ("plugins/LICENSE", "license, no extension"));  // a bare LICENSE file

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [mod]);

        // Only the mod survives; every doc, license and shortcut is dropped.
        InGame("plugins/StopThePed.dll").Should().BeTrue();
        InGame("plugins/readme.txt").Should().BeFalse();
        InGame("plugins/LICENSE.md").Should().BeFalse();
        InGame("plugins/Visit our site.url").Should().BeFalse();
        InGame("plugins/changelog.txt").Should().BeFalse();
        InGame("plugins/manual.pdf").Should().BeFalse();
        InGame("plugins/credits.rtf").Should().BeFalse();
        InGame("plugins/LICENSE").Should().BeFalse();
    }

    [Fact]
    public async Task A_real_config_ini_is_kept_not_mistaken_for_junk()
    {
        // A plain .ini that a mod reads must survive — only doc-named files go.
        var mod = Stage("stoptheped", ("plugins/StopThePed.dll", "code"), ("plugins/StopThePed.ini", "settings"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [mod]);

        InGame("plugins/StopThePed.ini").Should().BeTrue();
    }

    // ===== Redundant shared libraries =====================================

    [Fact]
    public async Task A_bundled_shared_library_is_stripped_from_a_mod_that_does_not_own_it()
    {
        // Stop The Ped ships a stale RAGENativeUI; the guide has you deselect it
        // by hand. The canonical copy comes from its own mod, so every bundled
        // one is dropped — under any of its spellings.
        var mod = Stage("stoptheped",
            ("plugins/StopThePed.dll", "code"),
            ("plugins/RAGENativeUI.dll", "stale"),
            ("plugins/RativeUI.dll", "stale"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [mod]);

        InGame("plugins/StopThePed.dll").Should().BeTrue();
        InGame("plugins/RAGENativeUI.dll").Should().BeFalse("a plugin's bundled copy is stripped");
        InGame("plugins/RativeUI.dll").Should().BeFalse("including the guide's shorthand name");
    }

    [Fact]
    public async Task The_owning_mod_keeps_its_own_shared_library()
    {
        // RAGENativeUI itself must, of course, still place RAGENativeUI.dll.
        var mod = Stage("ragenativeui", ("RAGENativeUI.dll", "the real one"));

        await _runner.RunAsync("run", _game, "standard", "1.0.3725", [mod]);

        InGame("RAGENativeUI.dll").Should().BeTrue();
    }

    // ===== OpenIV routing =================================================

    [Fact]
    public async Task OpenIV_files_are_set_aside_in_the_import_folder_not_the_game()
    {
        var mod = Stage("stoptheped",
            ("plugins/StopThePed.dll", "code"),
            ("textures/simpleMenu.ytd", "texture-bytes"),
            ("OpenIV.asi", "loader"),
            ("OpenCamera_x64.asi", "camera loader"),   // versioned OpenIV loader
            ("policepack.oiv", "an openiv package"));   // OpenIV package format

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [mod]);

        // The plugin still installs.
        InGame("plugins/StopThePed.dll").Should().BeTrue();

        // The texture, both OpenIV loaders and the .oiv package never reach the game.
        InGame("textures/simpleMenu.ytd").Should().BeFalse();
        InGame("OpenIV.asi").Should().BeFalse();
        InGame("OpenCamera_x64.asi").Should().BeFalse();
        InGame("policepack.oiv").Should().BeFalse();

        // They are set aside for the user, in one folder named after the mod (not
        // its id).
        var importDir = Path.Combine(_paths.OpenIvImportDirectory, "Stop The Ped");
        File.Exists(Path.Combine(importDir, "textures", "simpleMenu.ytd")).Should().BeTrue();
        File.Exists(Path.Combine(importDir, "OpenIV.asi")).Should().BeTrue();
        File.Exists(Path.Combine(importDir, "OpenCamera_x64.asi")).Should().BeTrue();
        File.Exists(Path.Combine(importDir, "policepack.oiv")).Should().BeTrue();
    }

    [Fact]
    public async Task A_manual_import_mod_lands_in_one_folder_named_after_the_mod_wrappers_stripped()
    {
        // A wrapper folder around the real content, plus a readme that is dropped.
        var mod = Stage("gtavremastered",
            ("GTA V Remastered v6.0/dlc.rpf", "package"),
            ("GTA V Remastered v6.0/x64/textures.rpf", "textures"),
            ("GTA V Remastered v6.0/readme.txt", "how to install"));

        await _runner.RunAsync("run", _game, "realism", "1.0.3725", [mod]);

        // Nothing reaches the game folder.
        Directory.EnumerateFiles(_game, "*", SearchOption.AllDirectories).Should().BeEmpty();

        // One folder, named by the mod, wrapper stripped so the content sits directly
        // inside rather than under "GTA V Remastered v6.0/".
        var importDir = Path.Combine(_paths.OpenIvImportDirectory, "GTA V Remastered Enhanced");
        File.Exists(Path.Combine(importDir, "dlc.rpf")).Should().BeTrue();
        File.Exists(Path.Combine(importDir, "x64", "textures.rpf")).Should().BeTrue();
        File.Exists(Path.Combine(importDir, "readme.txt")).Should().BeFalse("a readme is junk and never travels");
    }

    [Fact]
    public async Task A_normal_mod_asi_still_goes_to_the_game_not_the_openiv_folder()
    {
        // Simple Trainer's TrainerV.asi must never be mistaken for an OpenIV file.
        var mod = Stage("simpletrainer", ("TrainerV.asi", "trainer"), ("TrainerV.ini", "cfg"));

        await _runner.RunAsync("run", _game, "full-duty", "1.0.3725", [mod]);

        InGame("TrainerV.asi").Should().BeTrue("a normal .asi mod belongs in the game root");
        Directory.Exists(Path.Combine(_paths.OpenIvImportDirectory, "simpletrainer")).Should().BeFalse();
    }
}
