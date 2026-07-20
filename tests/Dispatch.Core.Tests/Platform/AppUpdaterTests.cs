using Dispatch.Core.Platform;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Platform;

/// <summary>
/// The no-op updater is what Core falls back to where self-update is not possible;
/// it must report unsupported and never pretend to stage anything.
/// </summary>
public sealed class AppUpdaterTests
{
    [Fact]
    public async Task NoAppUpdater_is_unsupported_and_stages_nothing()
    {
        var updater = new NoAppUpdater();

        updater.IsSupported.Should().BeFalse();
        (await updater.CheckDownloadAndStageAsync()).Should().BeNull();
    }
}
