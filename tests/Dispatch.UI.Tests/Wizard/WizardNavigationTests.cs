using Avalonia.Headless.XUnit;
using Dispatch.UI.Wizard;
using Dispatch.UI.Wizard.Steps;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Wizard;

/// <summary>
/// The wizard's navigation rules. Back must never destroy entered state, and
/// the last screen must not be a dead end — its action finishes the flow
/// rather than looking for a successor that does not exist.
/// </summary>
public sealed class WizardNavigationTests
{
    private static WizardViewModel Create() => new();

    [AvaloniaFact]
    public void Starts_on_the_first_screen()
    {
        var wizard = Create();

        wizard.CurrentIndex.Should().Be(0);
        wizard.CurrentStep.Should().BeOfType<WelcomeStep>();
        wizard.CanGoBack.Should().BeFalse("there is nothing before the first screen");
    }

    [AvaloniaFact]
    public void Has_six_screens_in_the_specified_order()
    {
        var wizard = Create();

        wizard.Steps.Select(step => step.GetType()).Should().Equal(
            typeof(WelcomeStep),
            typeof(WhatThisIsStep),
            typeof(LocateGameStep),
            typeof(ChoosePresetStep),
            typeof(InstallStep),
            typeof(OfficerStep));
    }

    [AvaloniaFact]
    public void The_last_screen_can_still_act()
    {
        // Regression: CanGoNext once required a following screen, which left
        // Finish permanently disabled on a completed flow with no way out.
        var wizard = Create();
        var officer = wizard.Steps.OfType<OfficerStep>().Single();

        wizard.GoTo(wizard.Steps.IndexOf(officer));
        officer.OfficerName = "J. Reyes";

        wizard.IsLastStep.Should().BeTrue();
        wizard.CanGoNext.Should().BeTrue("the last screen's action is Finish, not Next");
    }

    [AvaloniaFact]
    public void Finishing_the_last_screen_raises_completed_rather_than_navigating()
    {
        var wizard = Create();
        var officer = wizard.Steps.OfType<OfficerStep>().Single();
        var lastIndex = wizard.Steps.IndexOf(officer);

        wizard.GoTo(lastIndex);
        officer.OfficerName = "J. Reyes";

        var completed = false;
        wizard.Completed += (_, _) => completed = true;

        wizard.NextCommand.Execute(null);

        completed.Should().BeTrue();
        wizard.CurrentIndex.Should().Be(lastIndex, "finishing must not walk off the end");
    }

    [AvaloniaFact]
    public void Back_preserves_everything_entered()
    {
        // The wizard owns the data and screens bind to it, so moving backwards
        // changes an index and nothing else.
        var wizard = Create();
        var officer = wizard.Steps.OfType<OfficerStep>().Single();

        wizard.GoTo(wizard.Steps.IndexOf(officer));
        officer.OfficerName = "J. Reyes";
        officer.CallsignPhonetic = "LINCOLN";
        officer.CallsignBeat = 14;

        wizard.BackCommand.Execute(null);
        wizard.GoTo(wizard.Steps.IndexOf(officer));

        officer.OfficerName.Should().Be("J. Reyes");
        officer.CallsignPhonetic.Should().Be("LINCOLN");
        officer.CallsignBeat.Should().Be(14);
        officer.CallsignPreview.Should().Be("1 LINCOLN 14");
    }

    [AvaloniaFact]
    public void A_screen_becoming_satisfied_re_enables_the_footer()
    {
        var wizard = Create();
        var officer = wizard.Steps.OfType<OfficerStep>().Single();
        wizard.GoTo(wizard.Steps.IndexOf(officer));

        wizard.CanGoNext.Should().BeFalse("no officer name has been entered");

        officer.OfficerName = "J. Reyes";

        wizard.CanGoNext.Should().BeTrue("entering a name satisfies the screen");
    }

    [AvaloniaFact]
    public void The_install_screen_hides_its_navigation()
    {
        var wizard = Create();
        var install = wizard.Steps.OfType<InstallStep>().Single();

        install.ShowNavigation.Should().BeFalse(
            "there is nothing to go back to mid-run, and the run advances itself");
    }

    [AvaloniaFact]
    public void Going_out_of_range_is_ignored()
    {
        var wizard = Create();

        wizard.GoTo(-1);
        wizard.CurrentIndex.Should().Be(0);

        wizard.GoTo(wizard.Steps.Count + 5);
        wizard.CurrentIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public void The_preset_screen_preselects_the_recommended_setup()
    {
        // A recommendation that is not preselected is not a recommendation.
        var wizard = Create();
        var presets = wizard.Steps.OfType<ChoosePresetStep>().Single();

        presets.Selected.Should().NotBeNull();
        presets.Selected!.Tier.Should().Be(PresetTier.FullDuty);
        presets.Selected.Tagline.Should().Be("RECOMMENDED");
    }

    [AvaloniaFact]
    public void Every_selectable_preset_can_be_installed()
    {
        var wizard = Create();
        var presets = wizard.Steps.OfType<ChoosePresetStep>().Single();

        // Every preset now has a real lineup — selecting any of them lets the
        // flow advance (the required core is always ticked).
        foreach (var option in presets.Presets)
        {
            presets.Select(option);
            presets.CanAdvance.Should().BeTrue($"{option.Name} has a real mod lineup");
        }
    }

    [AvaloniaFact]
    public void An_invalid_game_folder_cannot_be_advanced_past()
    {
        var wizard = Create();
        var locate = wizard.Steps.OfType<LocateGameStep>().Single();

        locate.Selected = null;

        locate.CanAdvance.Should().BeFalse();
    }
}
