using Avalonia.Headless.XUnit;
using Dispatch.Core.Configuration;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The new launcher screens, driven through their view models against throwaway
/// temp storage. The profile totals derive from the career record, Settings
/// re-applies an edited identity into the real config files, and Mods lists what
/// is installed by checking the disk, removing reversibly via quarantine.
/// </summary>
public sealed class LauncherScreenTests : IDisposable
{
    private readonly string _root;

    public LauncherScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-screens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AppPaths Paths() => new(Path.Combine(_root, "app"), Path.Combine(_root, "tmp"));

    private sealed class FakeRecords(InstallRecord? record) : IInstallRecordStore
    {
        public Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(record);
    }

    private static OfficerProfile Officer() => new()
    {
        Id = Guid.NewGuid(),
        Name = "R. Vance",
        Agency = Agency.Lspd,
        CallsignDivision = 1,
        CallsignPhonetic = "ADAM",
        CallsignBeat = 7,
    };

    // ===== Profile ============================================

    [AvaloniaFact]
    public async Task Profile_derives_rank_and_cards_from_the_career_record()
    {
        var paths = Paths();
        var stats = new ProfileStatsStore(paths);
        await stats.RecordSessionAsync(new SessionStat(DateTimeOffset.UtcNow, 90 * 60, 10, 5, 2, 3));

        var vm = new ProfileViewModel(Officer(), _root, stats, new FakeRecords(null), paths);
        await vm.LoadAsync();

        vm.Initials.Should().Be("RV");
        vm.Rank.Should().Be("Sergeant", "90 hours crosses the sergeant threshold");
        vm.Cards.Should().Contain(c => c.Label == "HOURS ON DUTY" && c.Value == "90");
        vm.Cards.Should().Contain(c => c.Label == "ARRESTS" && c.Value == "5");
    }

    // ===== Settings ===========================================

    [AvaloniaFact]
    public async Task Settings_saves_the_officer_and_reapplies_the_callsign_to_the_ini()
    {
        // A fixture game folder with Grammar Police's config carrying an old callsign.
        var gpDir = Path.Combine(_root, "plugins", "LSPDFR", "GrammarPolice", "custom");
        Directory.CreateDirectory(gpDir);
        var gpFile = Path.Combine(gpDir, "config.ini");
        await File.WriteAllTextAsync(gpFile, "CallSign = 1 ADAM 7\n");

        var paths = Paths();
        var profiles = new ProfileStore(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileStore>.Instance);

        var vm = new SettingsViewModel(Officer(), _root, profiles, paths,
            quarantine: null, settingsWriter: new SettingsWriter());
        await vm.LoadAsync();

        vm.CallsignDivision = 2;
        vm.CallsignPhonetic = "LINCOLN";
        vm.CallsignBeat = 14;
        vm.CallsignPreview.Should().Be("2 LINCOLN 14");

        await vm.SaveOfficerCommand.ExecuteAsync(null);

        var doc = await IniDocument.LoadAsync(gpFile);
        doc.GetAnywhere("CallSign").Should().Be("2 LINCOLN 14", "the edited identity is written back in place");

        var saved = await profiles.LoadAsync();
        saved.ActiveOfficer!.Callsign.Should().Be("2 LINCOLN 14");
    }

    [AvaloniaFact]
    public async Task Settings_persists_the_reduced_motion_toggle()
    {
        var paths = Paths();
        var profiles = new ProfileStore(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileStore>.Instance);

        var vm = new SettingsViewModel(Officer(), _root, profiles, paths);
        await vm.LoadAsync();

        vm.ReducedMotion = true;
        await Task.Delay(50);

        (await profiles.LoadAsync()).Appearance.ReducedMotion.Should().BeTrue();
    }

    // ===== Mods ===============================================

    [AvaloniaFact]
    public async Task Mods_marks_present_files_installed_and_missing_files_removed()
    {
        // Two mods in the record; only the first's file exists on disk.
        Directory.CreateDirectory(Path.Combine(_root, "plugins"));
        await File.WriteAllTextAsync(Path.Combine(_root, "plugins", "here.dll"), "x");

        var record = new InstallRecord
        {
            GameBuild = "1.0.3725",
            ModIds = ["stoptheped", "gone"],
            Files =
            [
                new PlacedFile("plugins/here.dll", "hash", "stoptheped"),
                new PlacedFile("plugins/gone.dll", "hash", "gone"),
            ],
        };

        var vm = new ModsViewModel(_root, new FakeRecords(record), quarantine: null, paths: Paths());
        await vm.LoadAsync();

        vm.Rows.Should().Contain(r => r.Id == "stoptheped" && r.State == ModState.Installed);
        vm.Rows.Should().Contain(r => r.Id == "gone" && r.State == ModState.Missing);
    }

    [AvaloniaFact]
    public async Task Mods_remove_moves_a_mods_files_to_quarantine_reversibly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plugins"));
        await File.WriteAllTextAsync(Path.Combine(_root, "plugins", "StopThePed.dll"), "payload");

        var paths = Paths();
        var quarantine = new Dispatch.Core.Maintenance.Quarantine(
            paths.QuarantineDirectory,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Dispatch.Core.Maintenance.Quarantine>.Instance);

        var record = new InstallRecord
        {
            ModIds = ["stoptheped"],
            Files = [new PlacedFile("plugins/StopThePed.dll", "hash", "stoptheped")],
        };

        var vm = new ModsViewModel(_root, new FakeRecords(record), quarantine, paths);
        await vm.LoadAsync();

        var row = vm.Rows.First(r => r.Id == "stoptheped");
        await vm.RemoveCommand.ExecuteAsync(row);

        File.Exists(Path.Combine(_root, "plugins", "StopThePed.dll")).Should().BeFalse("the file moved to quarantine");
        var batches = await quarantine.ListBatchesAsync();
        batches.Should().ContainSingle();
        batches[0].Entries.Should().ContainSingle(e =>
            e.OriginalRelativePath.Contains("StopThePed.dll", StringComparison.OrdinalIgnoreCase));
    }
}
