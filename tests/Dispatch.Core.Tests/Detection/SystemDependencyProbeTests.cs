using Dispatch.Core.Detection;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Detection;

/// <summary>
/// The dependency probe names the runtimes the mod stack needs. Its detection is
/// Windows-only by design; off Windows it must report nothing rather than a wall of
/// false "missing" warnings, and every reported item must carry a real download link.
/// </summary>
public sealed class SystemDependencyProbeTests
{
    [Fact]
    public void Off_windows_it_reports_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        new SystemDependencyProbe().Check().Should().BeEmpty();
    }

    [Fact]
    public void On_windows_it_reports_the_three_runtimes_each_with_a_download_link()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var results = new SystemDependencyProbe().Check();

        results.Should().HaveCount(3);
        results.Should().OnlyContain(d => d.DownloadUrl.StartsWith("https://", StringComparison.Ordinal));
        results.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Name));
    }

    [Fact]
    public void A_missing_status_is_flagged_missing_and_an_installed_one_is_not()
    {
        var missing = new DependencyStatus("X", "why", DependencyState.Missing, "https://example.test");
        var installed = new DependencyStatus("Y", "why", DependencyState.Installed, "https://example.test");

        missing.IsMissing.Should().BeTrue();
        installed.IsMissing.Should().BeFalse();
    }
}
