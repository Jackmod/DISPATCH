using System.IO.Compression;
using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The acquirer's orchestration: reuse a cached archive when one exists, refuse a
/// mod no source can fetch, and extract straight into staging.
/// </summary>
public sealed class AcquirerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-acquire", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly StagingArea _staging;

    public AcquirerTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "appdata"), Path.Combine(_root, "temp"));
        _paths.EnsureCreated();
        _staging = new StagingArea(_paths.StagingRoot, "run-test");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static ModDefinition GithubMod() => new()
    {
        Id = "ragenativeui",
        Name = "RAGENativeUI",
        Source = SourceKind.GitHubRelease,
        Repo = "alexguirre/RAGENativeUI",
        Placement = new PlacementRule(PlacementKind.SingleFile, Files: ["RAGENativeUI.dll"]),
    };

    private static ModDefinition BrowserMod() => new()
    {
        Id = "lspdfr",
        Name = "LSPD First Response",
        Source = SourceKind.Browser,
        Placement = new PlacementRule(PlacementKind.RootAll),
    };

    private Acquirer BuildAcquirer(params IDownloadSource[] sources) =>
        new(sources, new ArchiveExtractor(NullLogger<ArchiveExtractor>.Instance), _paths, NullLogger<Acquirer>.Instance);

    private Acquirer BuildOfflineAcquirer(params IDownloadSource[] sources) =>
        new(sources, new ArchiveExtractor(NullLogger<ArchiveExtractor>.Instance), _paths,
            NullLogger<Acquirer>.Instance, new AcquisitionOptions(Offline: true));

    [Fact]
    public void Offline_ignores_network_sources_and_uses_only_the_pack()
    {
        // A GitHub source that must never be consulted offline.
        var github = new NeverCalledSource();

        var online = BuildAcquirer(github);
        online.CanAcquire(GithubMod()).Should().BeTrue("online, the GitHub source can fetch it");

        var offline = BuildOfflineAcquirer(github);
        offline.CanAcquire(GithubMod())
            .Should().BeFalse("offline, the network source is dropped and nothing else can fetch it");
    }

    private void SeedCache(string modId, string entryName, string content)
    {
        var dir = Path.Combine(_paths.ArchivesDirectory, modId);
        Directory.CreateDirectory(dir);

        using var stream = File.Create(Path.Combine(dir, "cached.zip"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void CanAcquire_is_true_for_a_github_mod_and_false_for_a_browser_mod()
    {
        var acquirer = BuildAcquirer(new NeverCalledSource());

        acquirer.CanAcquire(GithubMod()).Should().BeTrue();
        acquirer.CanAcquire(BrowserMod()).Should().BeFalse();
    }

    [Fact]
    public async Task A_cached_archive_is_reused_without_touching_any_source()
    {
        // If the source is consulted this throws; the cache must short-circuit it.
        SeedCache("ragenativeui", "RAGENativeUI.dll", "cached-bits");
        var source = new NeverCalledSource();
        var acquirer = BuildAcquirer(source);

        var acquired = await acquirer.AcquireAsync(GithubMod(), _staging);

        acquired.FromCache.Should().BeTrue();
        source.WasCalled.Should().BeFalse();
        File.ReadAllText(Path.Combine(acquired.StagedFolder, "RAGENativeUI.dll")).Should().Be("cached-bits");
    }

    [Fact]
    public async Task A_mod_with_no_capable_source_is_refused_with_a_reason()
    {
        var acquirer = BuildAcquirer(new NeverCalledSource());

        var act = () => acquirer.AcquireAsync(BrowserMod(), _staging);

        var ex = await act.Should().ThrowAsync<AcquisitionException>();
        ex.Which.ModId.Should().Be("lspdfr");
        ex.Which.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_downloaded_archive_is_extracted_into_staging()
    {
        var source = new FakeDownloadSource("RAGENativeUI.dll", "downloaded-bits");
        var acquirer = BuildAcquirer(source);

        var acquired = await acquirer.AcquireAsync(GithubMod(), _staging);

        acquired.FromCache.Should().BeFalse();
        source.WasCalled.Should().BeTrue();
        File.ReadAllText(Path.Combine(acquired.StagedFolder, "RAGENativeUI.dll")).Should().Be("downloaded-bits");
    }

    /// <summary>A source that fails the test if it is ever asked to download.</summary>
    private sealed class NeverCalledSource : IDownloadSource
    {
        public bool WasCalled { get; private set; }
        public SourceKind Kind => SourceKind.GitHubRelease;
        public bool CanHandle(ModDefinition mod) => mod.Source == SourceKind.GitHubRelease;

        public Task<DownloadResult> DownloadAsync(
            ModDefinition mod, string destinationDir, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            WasCalled = true;
            throw new InvalidOperationException("The source should not have been called.");
        }
    }

    /// <summary>A source that writes a zip into the cache instead of hitting the network.</summary>
    private sealed class FakeDownloadSource(string entryName, string content) : IDownloadSource
    {
        public bool WasCalled { get; private set; }
        public SourceKind Kind => SourceKind.GitHubRelease;
        public bool CanHandle(ModDefinition mod) => mod.Source == SourceKind.GitHubRelease;

        public Task<DownloadResult> DownloadAsync(
            ModDefinition mod, string destinationDir, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            WasCalled = true;
            Directory.CreateDirectory(destinationDir);
            var path = Path.Combine(destinationDir, "fetched.zip");

            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            return Task.FromResult(new DownloadResult(path, "v9.9", "https://example.test/fetched.zip"));
        }
    }
}
