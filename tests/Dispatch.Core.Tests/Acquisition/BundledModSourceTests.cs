using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The bundled pack source over the flat-dump model: archives are matched to mods
/// by name wherever they sit under the pack roots, and a match copies the archive
/// out for extraction.
/// </summary>
public sealed class BundledModSourceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-pack", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    // Real catalogue mods, since the source matches against the whole catalogue.
    private static ModDefinition Els => ModCatalogue.Mods["els"];
    private static ModDefinition StopThePed => ModCatalogue.Mods["stoptheped"];

    /// <summary>Drops an archive with a real download-style name into a folder.</summary>
    private string Drop(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "archive-bytes");
        return path;
    }

    private BundledModSource Source(params string[] roots) =>
        new(new ModPackRoots(roots), NullLogger<BundledModSource>.Instance);

    [Fact]
    public void CanHandle_true_once_a_matching_archive_is_dropped_anywhere_in_the_pack()
    {
        var pack = Path.Combine(_root, "pack");
        Directory.CreateDirectory(pack);
        var source = Source(pack);

        source.CanHandle(Els).Should().BeFalse();

        // Dropped loose in the pack, original name, in a preset subfolder — all fine.
        Drop(Path.Combine(pack, "2 - Plugins"), "ELS V1.05.rar");
        source.CanHandle(Els).Should().BeTrue();
    }

    [Fact]
    public void A_non_archive_file_is_ignored()
    {
        var pack = Path.Combine(_root, "pack");
        Drop(pack, "_WHAT-GOES-HERE.txt");

        Source(pack).CanHandle(Els).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_copies_the_matched_archive_and_reads_its_version()
    {
        var pack = Path.Combine(_root, "pack");
        Drop(pack, "ELS V1.05.rar");
        var dest = Path.Combine(_root, "out");

        var result = await Source(pack).DownloadAsync(Els, dest, progress: null, CancellationToken.None);

        File.Exists(result.ArchivePath).Should().BeTrue();
        Path.GetFileName(result.ArchivePath).Should().Be("ELS V1.05.rar");
        result.ResolvedVersion.Should().Be("V1.05");
        // The pack copy is untouched — it is the source of truth for the next run.
        File.Exists(Path.Combine(pack, "ELS V1.05.rar")).Should().BeTrue();
    }

    [Fact]
    public async Task Two_different_mods_dumped_together_each_match_their_own_archive()
    {
        var pack = Path.Combine(_root, "pack");
        Drop(pack, "ELS V1.05.rar");
        Drop(pack, "StopThePed 1.2.rar");
        var source = Source(pack);

        var els = await source.DownloadAsync(Els, Path.Combine(_root, "o1"), null, CancellationToken.None);
        var stp = await source.DownloadAsync(StopThePed, Path.Combine(_root, "o2"), null, CancellationToken.None);

        Path.GetFileName(els.ArchivePath).Should().Be("ELS V1.05.rar");
        Path.GetFileName(stp.ArchivePath).Should().Be("StopThePed 1.2.rar");
    }

    [Fact]
    public async Task The_user_root_overrides_the_shipped_root()
    {
        var userPack = Path.Combine(_root, "user");
        var shipped = Path.Combine(_root, "shipped");
        Drop(userPack, "ELS user.rar");
        Drop(shipped, "ELS shipped.rar");

        // User pack listed first; its archive wins for the same mod.
        var source = Source(userPack, shipped);
        var resolved = await source.DownloadAsync(Els, Path.Combine(_root, "o"), null, CancellationToken.None);

        Path.GetFileName(resolved.ArchivePath).Should().Be("ELS user.rar");
    }

    [Fact]
    public async Task A_mod_with_no_matching_archive_throws_a_reasoned_error()
    {
        var pack = Path.Combine(_root, "pack");
        Drop(pack, "SomethingUnrelated.zip");

        var act = () => Source(pack).DownloadAsync(Els, Path.Combine(_root, "o"), null, CancellationToken.None);

        await act.Should().ThrowAsync<AcquisitionException>();
    }
}
