using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The GitHub source's pure parts: parsing a release payload and choosing which
/// asset to pull. The HTTP itself is not exercised here — asset selection is
/// where the interesting decisions are, and it is deterministic.
/// </summary>
public sealed class GitHubReleaseSourceTests
{
    private const string SampleJson = """
    {
      "tag_name": "v3.6.0",
      "assets": [
        { "name": "ScriptHookVDotNet.zip", "browser_download_url": "https://example.test/shvdn.zip", "size": 1234 },
        { "name": "nightly.7z", "browser_download_url": "https://example.test/nightly.7z", "size": 999 },
        { "name": "source.tar.gz", "browser_download_url": "https://example.test/source.tar.gz", "size": 500 }
      ]
    }
    """;

    private static ModDefinition Mod(string? pattern = null) => new()
    {
        Id = "shvdn",
        Name = "Script Hook V .NET",
        Source = SourceKind.GitHubRelease,
        Repo = "scripthookvdotnet/scripthookvdotnet",
        AssetPattern = pattern,
        Placement = new PlacementRule(PlacementKind.NamedFiles, Files: ["ScriptHookVDotNet.asi"]),
    };

    [Fact]
    public void Parses_tag_and_assets()
    {
        var release = GitHubReleaseSource.ParseRelease(SampleJson);

        release.Should().NotBeNull();
        release!.TagName.Should().Be("v3.6.0");
        release.Assets.Should().HaveCount(3);
    }

    [Fact]
    public void With_no_pattern_prefers_the_zip()
    {
        var release = GitHubReleaseSource.ParseRelease(SampleJson)!;

        var asset = GitHubReleaseSource.SelectAsset(Mod(), release);

        asset!.Name.Should().Be("ScriptHookVDotNet.zip");
    }

    [Fact]
    public void A_pattern_selects_a_specific_asset()
    {
        var release = GitHubReleaseSource.ParseRelease(SampleJson)!;

        var asset = GitHubReleaseSource.SelectAsset(Mod(pattern: @"\.7z$"), release);

        asset!.Name.Should().Be("nightly.7z");
    }

    [Fact]
    public void An_unmatched_pattern_falls_back_to_the_zip()
    {
        var release = GitHubReleaseSource.ParseRelease(SampleJson)!;

        var asset = GitHubReleaseSource.SelectAsset(Mod(pattern: @"never-matches-this"), release);

        asset!.Name.Should().Be("ScriptHookVDotNet.zip");
    }

    [Fact]
    public void A_release_with_no_assets_selects_nothing()
    {
        var release = GitHubReleaseSource.ParseRelease("""{ "tag_name": "v1", "assets": [] }""")!;

        GitHubReleaseSource.SelectAsset(Mod(), release).Should().BeNull();
    }

    [Fact]
    public void CanHandle_requires_a_github_source_with_a_repo()
    {
        var source = new GitHubReleaseSource(
            new HttpFileDownloader(new System.Net.Http.HttpClient()),
            new System.Net.Http.HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubReleaseSource>.Instance);

        source.CanHandle(Mod()).Should().BeTrue();
        source.CanHandle(Mod() with { Repo = null }).Should().BeFalse();
        source.CanHandle(Mod() with { Source = SourceKind.Browser }).Should().BeFalse();
    }
}
