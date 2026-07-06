using System.Net;
using System.Text.Json;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Integration tests confirming YARP actually forwards proxied requests end-to-end — through
/// the gateway's own routing/authorization/rate-limiting pipeline — to the configured cluster
/// destination, and streams that destination's response back UNCHANGED (Docs/Tasks.md Phase 4
/// Tasks 5.1, 5.2, 5.5). <see cref="StubDownstreamServer"/> stands in for both the Auction and
/// Search Services (see its own remarks for why a real socket, not TestServer, is required).
/// </summary>
[Collection(GatewayServiceApiCollection.Name)]
public class ProxyRoutingTests(CustomWebAppFactory factory)
{
    // ── 5.1  GET /api/auctions — proxies to the Auction Service cluster ──────────
    [Fact]
    public async Task GetAuctions_ProxiesToAuctionCluster_ResponseUnchanged()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/auctions", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("auction-get", response.Headers.GetValues("X-Stub-Hit").Single());

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("auction-stub", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("GET", doc.RootElement.GetProperty("method").GetString());
    }

    // ── 5.2  GET /api/search — proxies to the Search Service cluster ─────────────
    [Fact]
    public async Task GetSearch_ProxiesToSearchCluster_ResponseUnchanged()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/search", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("search-get", response.Headers.GetValues("X-Stub-Hit").Single());

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("search-stub", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("GET", doc.RootElement.GetProperty("method").GetString());
    }

    // ── 5.5  Anonymous read routes — reachable with no bearer token at all ───────
    [Fact]
    public async Task GetAuctions_NoToken_ReachesStub_Returns200()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/auctions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSearch_NoToken_ReachesStub_Returns200()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/search", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
