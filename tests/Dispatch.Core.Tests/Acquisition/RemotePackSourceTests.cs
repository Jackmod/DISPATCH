using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The remote pack source matches hosted archive file names to mods with the same
/// matcher the bundled pack uses, so a thin installer can fetch each selected mod
/// by URL. Only the matching is exercised here; the byte transfer is
/// <see cref="HttpFileDownloader"/>'s concern.
/// </summary>
public sealed class RemotePackSourceTests
{
    private static ModDefinition Els => ModCatalogue.Mods["els"];
    private static ModDefinition StopThePed => ModCatalogue.Mods["stoptheped"];
    private static ModDefinition UltimateBackup => ModCatalogue.Mods["ultimatebackup"];

    private static RemotePackSource Source(params RemotePackEntry[] entries) =>
        new(new RemotePackIndex(entries),
            new HttpFileDownloader(new HttpClient()),
            NullLogger<RemotePackSource>.Instance);

    [Fact]
    public void CanHandle_true_when_a_hosted_archive_matches_the_mod()
    {
        var source = Source(
            new RemotePackEntry("ELS V1.05.rar", "https://example.com/a/ELS%20V1.05.rar"));

        source.CanHandle(Els).Should().BeTrue();
        source.CanHandle(StopThePed).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_false_when_the_index_is_empty()
    {
        Source().CanHandle(Els).Should().BeFalse();
    }

    [Fact]
    public void A_provider_sanitised_file_name_still_matches()
    {
        // GitHub rewrites spaces to dots in asset names; the matcher normalises
        // both sides, so the mod is still recognised.
        var source = Source(
            new RemotePackEntry("StopThePed.4.9.5.4.rar", "https://example.com/a/StopThePed.4.9.5.4.rar"));

        source.CanHandle(StopThePed).Should().BeTrue();
    }

    [Fact]
    public void Each_mod_matches_its_own_archive_when_several_are_hosted()
    {
        var source = Source(
            new RemotePackEntry("ELS V1.05.rar", "https://example.com/els.rar"),
            new RemotePackEntry("StopThePed_4.9.5.4.rar", "https://example.com/stp.rar"),
            new RemotePackEntry("UltimateBackup_1.8.7.1.rar", "https://example.com/ub.rar"));

        source.CanHandle(Els).Should().BeTrue();
        source.CanHandle(StopThePed).Should().BeTrue();
        source.CanHandle(UltimateBackup).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_throws_a_reasoned_error_for_an_unhosted_mod()
    {
        var source = Source(
            new RemotePackEntry("SomethingUnrelated.zip", "https://example.com/x.zip"));

        var act = () => source.DownloadAsync(Els, Path.GetTempPath(), progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<AcquisitionException>();
    }
}
