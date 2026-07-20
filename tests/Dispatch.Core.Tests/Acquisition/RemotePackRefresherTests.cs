using System.Net;
using Dispatch.Core.Acquisition;
using Dispatch.Core.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The refresher is what lets one installed thin installer keep up with the hosted
/// pack: a good manifest replaces the cache, and every failure is a no-op that
/// leaves the last good manifest — never a broken install.
/// </summary>
public sealed class RemotePackRefresherTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"dispatch-refresh-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network");
    }

    private RemotePackRefresher Refresher(HttpMessageHandler handler, bool offline = false) =>
        new(new HttpClient(handler),
            new AppPaths(Path.Combine(_root, "app"), Path.Combine(_root, "tmp")),
            new AcquisitionOptions(Offline: offline, ManifestUrl: "https://example.com/remote-pack.json"),
            NullLogger<RemotePackRefresher>.Instance);

    [Fact]
    public async Task A_valid_manifest_is_written_to_the_cache()
    {
        var refresher = Refresher(new StubHandler(HttpStatusCode.OK,
            "[{\"file\":\"ELS V1.05.rar\",\"url\":\"https://host/ELS.rar\"}]"));

        await refresher.RefreshAsync();

        File.Exists(refresher.CachePath).Should().BeTrue();
        File.ReadAllText(refresher.CachePath).Should().Contain("ELS V1.05.rar");
    }

    [Fact]
    public async Task A_network_failure_is_a_no_op_not_a_throw()
    {
        var refresher = Refresher(new ThrowingHandler());

        var act = () => refresher.RefreshAsync();

        await act.Should().NotThrowAsync();
        File.Exists(refresher.CachePath).Should().BeFalse();
    }

    [Fact]
    public async Task A_malformed_body_does_not_overwrite_a_good_cache()
    {
        var good = Refresher(new StubHandler(HttpStatusCode.OK,
            "[{\"file\":\"a.zip\",\"url\":\"https://h/a.zip\"}]"));
        await good.RefreshAsync();
        good.CachePath.Should().Match(p => File.Exists(p));

        // A rate-limit HTML page is served with 200 OK; it must not clobber the cache.
        var bad = Refresher(new StubHandler(HttpStatusCode.OK, "<html>rate limited</html>"));
        await bad.RefreshAsync();

        File.ReadAllText(bad.CachePath).Should().Contain("a.zip",
            "a malformed refresh keeps the last good manifest");
    }

    [Fact]
    public async Task Offline_skips_the_refresh_entirely()
    {
        // The throwing handler would surface if a fetch were attempted.
        var refresher = Refresher(new ThrowingHandler(), offline: true);

        await refresher.RefreshAsync();

        File.Exists(refresher.CachePath).Should().BeFalse();
    }
}
