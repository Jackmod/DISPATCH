using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The regression that matters most: the config catalogue applied against the mods'
/// REAL ini files actually changes their real keys. These tests run the whole install
/// config pass over copies of the shipped mod configs (in Fixtures/RealConfigs) and
/// assert the on-disk keys moved to the guide's values — the exact thing that silently
/// did nothing before, when the catalogue guessed key names that no file contained.
/// </summary>
public sealed class RealConfigApplyTests : IDisposable
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealConfigs");

    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-realcfg", Guid.NewGuid().ToString("N"));

    public RealConfigApplyTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_game))
            {
                Directory.Delete(_game, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Places a fixture ini at a game-relative path, as the installer would.</summary>
    private string Place(string fixture, string gameRelativePath)
    {
        var dest = Path.Combine(_game, gameRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(Path.Combine(FixtureDir, fixture), dest, overwrite: true);
        return dest;
    }

    private async Task<IniDocument> ApplyAndReload(string modId, string fixture, string gameRelativePath)
    {
        var dest = Place(fixture, gameRelativePath);
        var installer = new ConfigInstaller(
            new IniConfigWriter(NullLogger<IniConfigWriter>.Instance),
            NullLogger<ConfigInstaller>.Instance);

        await installer.ApplyAsync(_game, [modId], OfficerValues.Default);

        return await IniDocument.LoadAsync(dest);
    }

    [Fact]
    public async Task LSPDFR_keybinds_are_written_to_the_real_keys()
    {
        var doc = await ApplyAndReload("lspdfr", "lspdfr_keys.ini", "lspdfr/keys.ini");

        doc.GetAnywhere("PERFORM_ARREST_Key").Should().Be("I");
        doc.GetAnywhere("STOP_PEDS_Key").Should().Be("I");
        doc.GetAnywhere("TRAFFICSTOP_INTERACT_Key").Should().Be("I");
        doc.GetAnywhere("BACKUP_MENU_Key").Should().Be("None");
        doc.GetAnywhere("PURSUIT_MENU_ControllerKey").Should().Be("None");
        // Left Shift for the traffic stop is the community standard — left untouched.
        doc.GetAnywhere("TRAFFICSTOP_START_Key").Should().Be("LShiftKey");
    }

    [Fact]
    public async Task LSPDFR_settings_are_written_to_the_real_dotted_keys()
    {
        var doc = await ApplyAndReload("lspdfr", "lspdfr.ini", "lspdfr/lspdfr.ini");

        doc.GetAnywhere("Main.PreloadAllModels").Should().Be("false");
        doc.GetAnywhere("Chase.DisableCameraFocus").Should().Be("true");
        doc.GetAnywhere("Ambient.DisablePlayerFlashlightOverride").Should().Be("true");
    }

    [Fact]
    public async Task StopThePed_uses_its_real_key_names()
    {
        var doc = await ApplyAndReload("stoptheped", "StopThePed.ini", "plugins/LSPDFR/StopThePed.ini");

        doc.GetAnywhere("SearchKey").Should().Be("F9");
        doc.GetAnywhere("CallTransportKey").Should().Be("D9");
        doc.GetAnywhere("SprintBoostKey").Should().Be("A");
        doc.GetAnywhere("TakeOverAllArrests").Should().Be("no");
        doc.GetAnywhere("ForceSearchResultFullScreen").Should().Be("no");
        doc.GetAnywhere("RecruitNearestCopForTransport").Should().Be("yes");
    }

    [Fact]
    public async Task CompuLite_keys_and_parameters_are_written()
    {
        var doc = await ApplyAndReload("compulite", "CompuLite.ini", "plugins/LSPDFR/CompuLite.ini");

        doc.GetAnywhere("OpenComputerKey").Should().Be("X");
        doc.GetAnywhere("GiveCitationKey").Should().Be("X");
        doc.GetAnywhere("GiveCitationModifierKey").Should().Be("LShiftKey");
        doc.GetAnywhere("CourtCaseWaitingTime").Should().Be("24");
        doc.GetAnywhere("IsPausedWhenOpen").Should().Be("no");
    }

    [Fact]
    public async Task CalloutInterface_menu_mdt_and_autotab_are_written()
    {
        var doc = await ApplyAndReload("calloutinterface", "CalloutInterface.ini", "plugins/LSPDFR/CalloutInterface.ini");

        doc.GetAnywhere("CalloutMenuKey").Should().Be("F10");
        doc.GetAnywhere("ToggleTerminalKey").Should().Be("NumPad7");
        doc.GetAnywhere("MDTCallsign").Should().Be("1 ADAM 7");
        doc.GetAnywhere("PostalCodeEnabled").Should().Be("True");
        // The [AutoTab] block, scoped so its generic keys resolve correctly.
        doc.Get("AutoTab", "Peds").Should().Be("True");
        doc.Get("AutoTab", "Vehicles").Should().Be("True");
    }

    [Fact]
    public async Task GrammarPolice_identity_and_keys_are_written()
    {
        var doc = await ApplyAndReload("grammarpolice", "GrammarPolice_default.ini",
            "plugins/LSPDFR/GrammarPolice/default.ini");

        doc.GetAnywhere("Callsign").Should().Be("\"1 ADAM 7\"");
        doc.GetAnywhere("AgencyCodes").Should().Be("\"IMMERSIVE\"");
        doc.GetAnywhere("InterfaceKey").Should().Be("F8");
        doc.GetAnywhere("SettingsKey").Should().Be("F7");
        doc.GetAnywhere("RadioKey").Should().Be("O");
        doc.GetAnywhere("PrefaceResponse").Should().Be("2");
    }

    [Fact]
    public async Task Spotlight_section_scoped_toggles_land_in_the_right_section()
    {
        var doc = await ApplyAndReload("spotlight", "Spotlight_General.ini",
            "plugins/spotlight_resources/General.ini");

        doc.GetAnywhere("EditorKey").Should().Be("F6");
        doc.Get("Keyboard", "Toggle").Should().Be("S");
        doc.Get("Mouse", "Toggle").Should().Be("S");
        doc.Get("Controller", "Modifier").Should().Be("None");
    }

    [Fact]
    public async Task SimpleHUD_writes_only_the_settings_this_version_still_has()
    {
        var doc = await ApplyAndReload("simplehud", "SimpleHUD.ini", "SimpleHUD.ini");

        doc.GetAnywhere("PostalEnabled").Should().Be("false");
        doc.GetAnywhere("TimeFormat").Should().Be("12h");
        doc.Get("Menu", "ToggleKey").Should().Be("B");
    }

    [Fact]
    public async Task SpeedRadar_matches_the_mods_own_misspelled_key()
    {
        var doc = await ApplyAndReload("speedradarlite", "SpeedRadarLite.ini",
            "plugins/LSPDFR/SpeedRadarLite.ini");

        doc.GetAnywhere("IncreaseThreashold").Should().Be("I");
        doc.GetAnywhere("DecreaseThreasholdKey").Should().Be("O");
        doc.GetAnywhere("SpeedThreshold").Should().Be("55");
    }

    [Fact]
    public async Task The_keybind_editor_writes_the_suggested_scheme_into_the_real_files()
    {
        // The other half of the fix: the control catalogue (the launcher's keybind
        // editor) applied against the real ini files changes the real keys, and puts
        // a modifier in the companion field the file actually uses.
        Place("lspdfr_keys.ini", "lspdfr/keys.ini");
        Place("StopThePed.ini", "plugins/LSPDFR/StopThePed.ini");
        Place("CompuLite.ini", "plugins/LSPDFR/CompuLite.ini");

        var writer = new ControlWriter();
        var scheme = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(b => b.Action.Id is "lspdfr.arrest" or "lspdfr.backup"
                or "stp.patdown" or "stp.transport" or "compulite.citation")
            .ToList();

        await writer.WriteAsync(_game, scheme);

        var keys = await IniDocument.LoadAsync(Path.Combine(_game, "lspdfr", "keys.ini"));
        keys.GetAnywhere("PERFORM_ARREST_Key").Should().Be("E");
        keys.GetAnywhere("BACKUP_MENU_Key").Should().Be("B");

        var stp = await IniDocument.LoadAsync(Path.Combine(_game, "plugins", "LSPDFR", "StopThePed.ini"));
        stp.GetAnywhere("SearchKey").Should().Be("F9");
        stp.GetAnywhere("CallTransportKey").Should().Be("D9");

        // Compulite citation is Left Shift + X — modifier in its real companion field.
        var compulite = await IniDocument.LoadAsync(Path.Combine(_game, "plugins", "LSPDFR", "CompuLite.ini"));
        compulite.GetAnywhere("GiveCitationKey").Should().Be("X");
        compulite.GetAnywhere("GiveCitationModifierKey").Should().Be("LShiftKey");
    }

    [Fact]
    public async Task Verify_confirms_a_correctly_applied_config()
    {
        Place("StopThePed.ini", "plugins/LSPDFR/StopThePed.ini");
        var installer = new ConfigInstaller(
            new IniConfigWriter(NullLogger<IniConfigWriter>.Instance),
            NullLogger<ConfigInstaller>.Instance);

        await installer.ApplyAsync(_game, ["stoptheped"], OfficerValues.Default);
        var report = await installer.VerifyAsync(_game, ["stoptheped"], OfficerValues.Default);

        report.AllApplied.Should().BeTrue("every applied value should read back correctly");
        report.Mismatches.Should().BeEmpty();
        report.VerifiedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Verify_catches_a_value_that_did_not_take()
    {
        var dest = Place("StopThePed.ini", "plugins/LSPDFR/StopThePed.ini");
        var installer = new ConfigInstaller(
            new IniConfigWriter(NullLogger<IniConfigWriter>.Instance),
            NullLogger<ConfigInstaller>.Instance);

        await installer.ApplyAsync(_game, ["stoptheped"], OfficerValues.Default);

        // Simulate something clobbering a value after apply (a bad write, a mod
        // rewriting its own ini) — the safeguard must catch it.
        var doc = await IniDocument.LoadAsync(dest);
        doc.SetAnywhere("SearchKey", "F1");
        await doc.SaveAsync(dest);

        var report = await installer.VerifyAsync(_game, ["stoptheped"], OfficerValues.Default);

        report.AllApplied.Should().BeFalse();
        report.Mismatches.Should().Contain(m =>
            m.Check.Setting == "SearchKey" && m.Check.Expected == "F9" && m.Check.Actual == "F1");
    }

    [Fact]
    public async Task The_keybind_editor_verify_confirms_binds_and_catches_a_bad_one()
    {
        Place("lspdfr_keys.ini", "lspdfr/keys.ini");
        var writer = new ControlWriter();
        var scheme = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(b => b.Action.Id is "lspdfr.arrest" or "lspdfr.backup")
            .ToList();

        await writer.WriteAsync(_game, scheme);

        var good = await writer.VerifyAsync(_game, scheme);
        good.Should().OnlyContain(c => c.Result == BindCheckResult.Verified);

        // Now clobber one bind on disk and re-verify.
        var keys = Path.Combine(_game, "lspdfr", "keys.ini");
        var doc = await IniDocument.LoadAsync(keys);
        doc.SetAnywhere("PERFORM_ARREST_Key", "Z");
        await doc.SaveAsync(keys);

        var after = await writer.VerifyAsync(_game, scheme);
        after.Should().Contain(c => c.ActionId == "lspdfr.arrest" && c.Result == BindCheckResult.Mismatch);
    }

    [Fact]
    public async Task A_nonexistent_key_is_never_added_to_the_file()
    {
        // The writer must only change keys the file already has. Applying LSPDFR's
        // settings to its keys file (wrong file on purpose) must add nothing.
        var dest = Place("lspdfr_keys.ini", "lspdfr/keys.ini");
        var before = await File.ReadAllTextAsync(dest);

        var writer = new IniConfigWriter(NullLogger<IniConfigWriter>.Instance);
        var doc = await IniDocument.LoadAsync(dest);
        var result = writer.Apply(doc, [new ConfigSetting("Totally.Made.Up.Key", "x")], OfficerValues.Default);
        await doc.SaveAsync(dest);

        result.Changed.Should().BeFalse();
        (await File.ReadAllTextAsync(dest)).Should().Be(before);
    }
}
