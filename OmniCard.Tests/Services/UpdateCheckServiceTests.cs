using System.Net;
using System.Net.Http;
using System.Text;
using OmniCard.Collection;
using Xunit;

namespace OmniCard.Tests.Services;

public class UpdateCheckServiceTests
{
    // --- IsNewer: the pure comparison used to decide whether to show the notice ---

    [Theory]
    [InlineData("1.2.0", "1.3.0", true)]   // newer minor
    [InlineData("1.2.0", "2.0.0", true)]   // newer major
    [InlineData("1.2.0", "1.2.1", true)]   // newer patch
    [InlineData("1.2.0", "1.2.0", false)]  // same
    [InlineData("1.3.0", "1.2.0", false)]  // older remote
    public void IsNewer_ComparesNumericCore(string current, string latest, bool expected)
        => Assert.Equal(expected, UpdateCheckService.IsNewer(current, latest));

    [Theory]
    [InlineData("v1.2.0", "v1.3.0", true)]   // leading v on both
    [InlineData("1.2.0", "v1.3.0", true)]    // leading v on one
    [InlineData("v1.2.0", "v1.2.0", false)]  // same with prefix
    public void IsNewer_IgnoresLeadingV(string current, string latest, bool expected)
        => Assert.Equal(expected, UpdateCheckService.IsNewer(current, latest));

    [Fact]
    public void IsNewer_DevBuildAheadOfRelease_IsNotNewer()
    {
        // A local build 3 commits past the v1.2.0 tag reports 1.2.1-alpha.0.3 (+sha). The latest
        // published release is still 1.2.0 — the dev build must NOT be told to "update".
        Assert.False(UpdateCheckService.IsNewer("1.2.1-alpha.0.3+abc123", "v1.2.0"));
    }

    [Fact]
    public void IsNewer_PreReleaseSuffixOnLatestUsesNumericCore()
    {
        // Suffixes are ignored: 1.3.0-rc.1's core (1.3.0) is newer than 1.2.0.
        Assert.True(UpdateCheckService.IsNewer("1.2.0", "v1.3.0-rc.1"));
    }

    [Theory]
    [InlineData("1.2", "1.3")]     // two-part versions
    [InlineData("v1", "v2")]       // single-part (normalized to x.0)
    public void IsNewer_HandlesShortVersions(string current, string latest)
        => Assert.True(UpdateCheckService.IsNewer(current, latest));

    [Theory]
    [InlineData(null, "1.2.0")]
    [InlineData("1.2.0", null)]
    [InlineData("", "1.2.0")]
    [InlineData("not-a-version", "1.2.0")]
    [InlineData("1.2.0", "garbage")]
    public void IsNewer_FailsClosedOnBadInput(string? current, string? latest)
        => Assert.False(UpdateCheckService.IsNewer(current, latest));

    // --- CheckForUpdateAsync: end-to-end against a faked GitHub response ---

    [Fact]
    public async Task CheckForUpdateAsync_NewerReleaseAvailable_ReportsUpdate()
    {
        var json = """
        { "tag_name": "v1.3.0", "html_url": "https://github.com/anubisascends/OmniCard/releases/tag/v1.3.0" }
        """;
        var service = new UpdateCheckService(FactoryReturning(HttpStatusCode.OK, json));

        var result = await service.CheckForUpdateAsync("1.2.0");

        Assert.NotNull(result);
        Assert.True(result!.UpdateAvailable);
        Assert.Equal("1.2.0", result.CurrentVersion);
        Assert.Equal("1.3.0", result.LatestVersion);
        Assert.Equal("https://github.com/anubisascends/OmniCard/releases/tag/v1.3.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameVersion_NoUpdate()
    {
        var json = """{ "tag_name": "v1.2.0", "html_url": "https://example.com/r" }""";
        var service = new UpdateCheckService(FactoryReturning(HttpStatusCode.OK, json));

        var result = await service.CheckForUpdateAsync("1.2.0");

        Assert.NotNull(result);
        Assert.False(result!.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_MissingReleaseUrl_FallsBackToReleasesLatest()
    {
        var json = """{ "tag_name": "v1.3.0" }""";
        var service = new UpdateCheckService(FactoryReturning(HttpStatusCode.OK, json));

        var result = await service.CheckForUpdateAsync("1.2.0");

        Assert.NotNull(result);
        Assert.Equal("https://github.com/anubisascends/OmniCard/releases/latest", result!.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpError_ReturnsNull()
    {
        var service = new UpdateCheckService(FactoryReturning(HttpStatusCode.ServiceUnavailable, ""));
        Assert.Null(await service.CheckForUpdateAsync("1.2.0"));
    }

    [Fact]
    public async Task CheckForUpdateAsync_Throwing_ReturnsNull()
    {
        // Network failure must be swallowed to null, never surfaced.
        var service = new UpdateCheckService(new ThrowingHttpClientFactory());
        Assert.Null(await service.CheckForUpdateAsync("1.2.0"));
    }

    // --- Test helpers ---

    private static IHttpClientFactory FactoryReturning(HttpStatusCode status, string body)
        => new StubHttpClientFactory(new StubHandler(status, body));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new HttpRequestException("network down");
    }
}
