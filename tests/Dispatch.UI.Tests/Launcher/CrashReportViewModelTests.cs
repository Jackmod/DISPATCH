using Avalonia.Headless.XUnit;
using Dispatch.UI.Launcher;
using FluentAssertions;
using Xunit;

namespace Dispatch.UI.Tests.Launcher;

/// <summary>
/// The crash-report tool: paste a game log and get the cause in plain language, and
/// when nothing known matches, say so rather than pretend — never crash over a paste.
/// </summary>
public sealed class CrashReportViewModelTests
{
    [AvaloniaFact]
    public void Analysing_a_recognised_log_surfaces_the_cause()
    {
        var vm = new CrashReportViewModel
        {
            LogText = "[ERROR] GrammarPolice could not be loaded because RageNativeUI is missing",
        };

        vm.AnalyzeCommand.Execute(null);

        vm.HasFindings.Should().BeTrue();
        vm.Findings.Should().Contain(f => f.Title.Contains("failed to load"));
        vm.NoKnownIssues.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Analysing_unrecognised_text_reports_no_known_issue_without_crashing()
    {
        var vm = new CrashReportViewModel { LogText = "just some text that matches nothing at all" };

        vm.AnalyzeCommand.Execute(null);

        vm.HasFindings.Should().BeFalse();
        vm.NoKnownIssues.Should().BeTrue();
        vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public void Clearing_resets_the_log_and_findings()
    {
        var vm = new CrashReportViewModel
        {
            LogText = "GrammarPolice could not be loaded because RageNativeUI is missing",
        };
        vm.AnalyzeCommand.Execute(null);
        vm.HasFindings.Should().BeTrue();

        vm.ClearCommand.Execute(null);

        vm.LogText.Should().BeEmpty();
        vm.HasFindings.Should().BeFalse();
        vm.NoKnownIssues.Should().BeFalse();
    }

    [AvaloniaFact]
    public void The_view_loads_with_the_crash_report_view_model()
    {
        // Instantiating the real view loads its XAML against the app resources, so a
        // broken resource key or binding fails here.
        var view = new CrashReportView { DataContext = new CrashReportViewModel() };

        view.Should().NotBeNull();
    }
}
