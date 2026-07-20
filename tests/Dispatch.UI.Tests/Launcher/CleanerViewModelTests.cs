using Avalonia.Headless.XUnit;
using Dispatch.Core.Maintenance;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The cleaner VM gates confirmation on both having scrolled to the bottom and
/// having something selected, and it moves files to quarantine rather than
/// deleting. These are the interaction rules that keep the most dangerous
/// feature from acting on a glance.
/// </summary>
public sealed class CleanerViewModelTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-cleaner-vm", Guid.NewGuid().ToString("N"));

    private readonly string _quarantineRoot;
    private readonly CleanerViewModel _vm;

    public CleanerViewModelTests()
    {
        Directory.CreateDirectory(_game);
        _quarantineRoot = Path.Combine(_game, "..", "q-" + Guid.NewGuid().ToString("N"));

        var cleaner = new FolderCleaner(NullLogger<FolderCleaner>.Instance, ["plugins/Known.dll"]);
        var quarantine = new Quarantine(_quarantineRoot, NullLogger<Quarantine>.Instance);
        _vm = new CleanerViewModel(cleaner, quarantine);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _game, _quarantineRoot })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    private void Given(string relative, string content = "x")
    {
        var full = Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [AvaloniaFact]
    public async Task Scanning_populates_the_three_tiers()
    {
        Given("GTA5.exe");
        Given("plugins/Known.dll");
        Given("plugins/Unknown.dll");
        Given("mystery.dat");

        await _vm.ScanAsync(_game);

        _vm.Stage.Should().Be(CleanerStage.Preview);
        _vm.Tiers.Should().HaveCount(3);
        _vm.Tiers.Single(t => t.Tier == CleanTier.Known).Rows.Should().ContainSingle();
        _vm.Tiers.Single(t => t.Tier == CleanTier.Unknown).Rows.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task Known_and_likely_are_preselected_unknown_is_not()
    {
        Given("plugins/Known.dll");
        Given("plugins/Unknown.dll");
        Given("mystery.dat");

        await _vm.ScanAsync(_game);

        var unknown = _vm.Tiers.Single(t => t.Tier == CleanTier.Unknown).Rows[0];
        unknown.Selected.Should().BeFalse("the unknown tier is never preselected");

        var known = _vm.Tiers.Single(t => t.Tier == CleanTier.Known).Rows[0];
        known.Selected.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Confirm_is_blocked_until_scrolled_to_the_bottom()
    {
        Given("plugins/Known.dll");
        await _vm.ScanAsync(_game);

        _vm.SelectedCount.Should().BeGreaterThan(0);
        _vm.CanConfirm.Should().BeFalse("the user has not scrolled to the bottom yet");

        _vm.MarkScrolledToBottomCommand.Execute(null);

        _vm.CanConfirm.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Confirm_is_blocked_when_nothing_is_selected()
    {
        Given("mystery.dat"); // unknown, not preselected
        await _vm.ScanAsync(_game);
        _vm.MarkScrolledToBottomCommand.Execute(null);

        _vm.CanConfirm.Should().BeFalse("nothing is selected to remove");
    }

    [AvaloniaFact]
    public async Task Confirming_moves_selected_files_to_quarantine_not_the_bin()
    {
        Given("plugins/Known.dll", "payload");
        await _vm.ScanAsync(_game);
        _vm.MarkScrolledToBottomCommand.Execute(null);

        await _vm.ConfirmCommand.ExecuteAsync(null);

        _vm.Stage.Should().Be(CleanerStage.Done);
        File.Exists(Path.Combine(_game, "plugins", "Known.dll")).Should().BeFalse("it was moved out");
        _vm.Summary.Should().Contain("quarantine").And.Contain("restore");
    }

    [AvaloniaFact]
    public async Task Deselecting_everything_disables_confirm_again()
    {
        Given("plugins/Known.dll");
        await _vm.ScanAsync(_game);
        _vm.MarkScrolledToBottomCommand.Execute(null);
        _vm.CanConfirm.Should().BeTrue();

        foreach (var row in _vm.Tiers.SelectMany(t => t.Rows))
        {
            row.Selected = false;
        }

        _vm.CanConfirm.Should().BeFalse("nothing remains selected");
    }
}
