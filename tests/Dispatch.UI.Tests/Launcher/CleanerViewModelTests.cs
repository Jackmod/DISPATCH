using Avalonia.Headless.XUnit;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The cleaner runs as a short guided sequence: scan against the install record,
/// warn with the exact list, clean to quarantine, then gate the close on the
/// user confirming they verified their game files. These tests cover that flow
/// and the safety rules that survive the redesign — unknown files are never
/// queued, and files move rather than delete.
/// </summary>
public sealed class CleanerViewModelTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-cleaner-vm", Guid.NewGuid().ToString("N"));

    private readonly string _quarantineRoot;
    private readonly Quarantine _quarantine;

    public CleanerViewModelTests()
    {
        Directory.CreateDirectory(_game);
        _quarantineRoot = Path.Combine(_game, "..", "q-" + Guid.NewGuid().ToString("N"));
        _quarantine = new Quarantine(_quarantineRoot, NullLogger<Quarantine>.Instance);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _game, _quarantineRoot })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    private CleanerViewModel Vm(params string[] recordedFiles) =>
        new(_quarantine, new FakeRecords(recordedFiles));

    private void Given(string relative, string content = "x")
    {
        var full = Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [AvaloniaFact]
    public async Task Scanning_lands_on_the_warning_step_and_queues_only_mod_files()
    {
        Given("GTA5.exe");            // stock
        Given("plugins/mod.dll");     // likely
        Given("mystery.dat");         // unknown

        var vm = Vm();
        await vm.ScanAsync(_game);

        vm.Stage.Should().Be(CleanerStage.Warning);
        vm.HasWork.Should().BeTrue();
        vm.ToRemove.Select(r => r.Path).Should().Contain("plugins/mod.dll");
        vm.ToRemove.Select(r => r.Path).Should().NotContain("mystery.dat", "unknown files are never queued");
        vm.ToRemove.Select(r => r.Path).Should().NotContain("gta5.exe");
    }

    [AvaloniaFact]
    public async Task The_install_record_makes_its_own_files_known()
    {
        Given("plugins/StopThePed.dll");

        var vm = Vm("plugins/StopThePed.dll");
        await vm.ScanAsync(_game);

        var row = vm.ToRemove.Single();
        row.Candidate.Tier.Should().Be(CleanTier.Known, "the record proves Dispatch placed it");
    }

    [AvaloniaFact]
    public async Task A_clean_folder_reports_nothing_to_do()
    {
        Given("GTA5.exe");
        Given("update/update.rpf");
        Given("x64a.rpf"); // a root archive the manifest now recognises as stock

        var vm = Vm();
        await vm.ScanAsync(_game);

        vm.Stage.Should().Be(CleanerStage.Warning);
        vm.HasWork.Should().BeFalse();
        vm.ToRemove.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Proceeding_moves_files_to_quarantine_not_the_bin()
    {
        Given("plugins/mod.dll", "payload");

        var vm = Vm();
        await vm.ScanAsync(_game);

        await vm.ProceedCommand.ExecuteAsync(null);

        vm.Stage.Should().Be(CleanerStage.Cleaning);
        vm.CleaningInProgress.Should().BeFalse("the move has finished");
        File.Exists(Path.Combine(_game, "plugins", "mod.dll")).Should().BeFalse("it was moved out");
        vm.CleanSummary.Should().Contain("quarantine").And.Contain("restore");
    }

    [AvaloniaFact]
    public async Task Continue_advances_from_cleaning_to_verify()
    {
        Given("plugins/mod.dll");

        var vm = Vm();
        await vm.ScanAsync(_game);
        await vm.ProceedCommand.ExecuteAsync(null);

        vm.ContinueCommand.Execute(null);

        vm.Stage.Should().Be(CleanerStage.Verify);
    }

    [AvaloniaFact]
    public async Task Closing_is_gated_on_acknowledging_the_verify_step()
    {
        Given("plugins/mod.dll");

        var vm = Vm();
        await vm.ScanAsync(_game);
        await vm.ProceedCommand.ExecuteAsync(null);
        vm.ContinueCommand.Execute(null);

        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.CanClose.Should().BeFalse("verify has not been ticked");
        vm.CloseCommand.Execute(null);
        closed.Should().BeFalse();

        vm.VerifyAcknowledged = true;

        vm.CanClose.Should().BeTrue();
        vm.CloseCommand.Execute(null);
        closed.Should().BeTrue();
    }

    /// <summary>An install record store returning a fixed file list.</summary>
    private sealed class FakeRecords(params string[] files) : IInstallRecordStore
    {
        public Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<InstallRecord?>(new InstallRecord
            {
                Files = files.Select(f => new PlacedFile(f, "hash", "mod")).ToList(),
            });
    }
}
