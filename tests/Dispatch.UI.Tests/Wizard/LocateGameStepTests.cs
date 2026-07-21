using Avalonia.Headless.XUnit;
using Dispatch.Core.Platform;
using Dispatch.UI.Wizard.Steps;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Wizard;

/// <summary>
/// The Locate screen's actions: browsing to a folder validates it, and the
/// cleaner overlay opens and closes. These exist because the buttons on this
/// screen were once inert — wired to nothing — and this pins them to behaviour.
/// </summary>
public sealed class LocateGameStepTests
{
    [AvaloniaFact]
    public void Browsing_to_a_non_game_folder_adds_it_as_invalid_and_selects_it()
    {
        var step = new LocateGameStep();
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-not-a-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            step.AddFolder(temp);

            step.Selected!.Path.Should().Be(temp);
            step.Selected.State.Should().Be(GameCandidateState.Invalid, "there is no GTA5.exe there");
            step.CanAdvance.Should().BeFalse("an invalid folder cannot be advanced past");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Browsing_to_a_folder_with_GTA5_exe_adds_it_as_a_usable_candidate()
    {
        var step = new LocateGameStep();
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-fake-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "GTA5.exe"), "stub");

        try
        {
            step.AddFolder(temp);

            step.Selected!.Path.Should().Be(temp);
            step.Selected.State.Should().NotBe(GameCandidateState.Invalid);

            // A valid folder is not enough on its own — the integrity check must
            // be acknowledged before the screen will advance.
            step.CanAdvance.Should().BeFalse("game files have not been verified yet");
            step.IntegrityVerified = true;
            step.CanAdvance.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [AvaloniaFact]
    public void An_Enhanced_folder_is_recognised_and_cannot_be_advanced_past()
    {
        // GTA5_Enhanced.exe but no GTA5.exe: a real GTA V folder, but the edition
        // the mod stack cannot run on. It must be flagged and blocked even if the
        // user ticks the verify box.
        var step = new LocateGameStep();
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-enhanced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "GTA5_Enhanced.exe"), "stub");

        try
        {
            step.AddFolder(temp);

            step.Selected!.State.Should().Be(GameCandidateState.WrongEdition);
            step.SelectedIsEnhanced.Should().BeTrue();
            step.SelectedIsLegacy.Should().BeFalse();

            step.IntegrityVerified = true;
            step.CanAdvance.Should().BeFalse("the Enhanced edition can never run the mods");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_Legacy_folder_is_labelled_Legacy()
    {
        var step = new LocateGameStep();
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "GTA5.exe"), "stub");

        try
        {
            step.AddFolder(temp);

            step.SelectedIsLegacy.Should().BeTrue();
            step.SelectedEdition.Should().Be("Legacy");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_verify_steps_adapt_to_the_platform()
    {
        var step = new LocateGameStep();
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-steam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "GTA5.exe"), "stub");

        try
        {
            step.AddFolder(temp);   // added as a "Manual" candidate

            step.VerifySteps.Should().NotBeEmpty();
            step.VerifySteps[0].Number.Should().Be(1);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // A Defender service that fails exactly the way declining a UAC prompt does.
    private sealed class ThrowingDefender : IDefenderService
    {
        public bool IsAvailable => true;

        public Task<bool?> IsExcludedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<bool?>(false);

        public Task<bool> AddExclusionAsync(string path, CancellationToken cancellationToken = default) =>
            throw new System.ComponentModel.Win32Exception(1223, "The operation was canceled by the user.");
    }

    [AvaloniaFact]
    public async Task Adding_a_defender_exclusion_that_fails_never_crashes_the_wizard()
    {
        var step = new LocateGameStep(defender: new ThrowingDefender());
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-defender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "GTA5.exe"), "stub");

        try
        {
            step.AddFolder(temp);

            // Declining UAC raises a Win32Exception; it must be swallowed, not crash.
            var act = () => step.AddDefenderExclusionAsync();
            await act.Should().NotThrowAsync();

            step.DefenderBusy.Should().BeFalse("the busy flag is always cleared");
            step.DefenderStatus.Should().Contain("later");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_cleaner_overlay_starts_closed_and_closes_on_request()
    {
        var step = new LocateGameStep();

        step.IsCleanerOpen.Should().BeFalse("the cleaner is not shown until asked for");

        step.CloseCleaner();
        step.IsCleanerOpen.Should().BeFalse();
    }
}
