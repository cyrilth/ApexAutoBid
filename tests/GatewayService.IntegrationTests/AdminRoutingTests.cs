using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Integration tests for the gateway's admin routing (Docs/Tasks.md Phase 11 Task 7):
/// api/admin/* is routed by resource segment to the owning service's cluster, and every such
/// route requires the "admin" role claim at the edge (Program.cs's "admin" AuthorizationPolicy)
/// — anonymous callers get a gateway-generated 401 problem+json (same OnChallenge wiring
/// <see cref="AuthenticationTests"/> exercises on the pre-existing "authenticated" policy),
/// authenticated non-admin callers get a gateway-generated 403 problem+json (OnForbidden), and
/// admin callers reach the correct downstream stub per <see cref="StubDownstreamServer"/>'s
/// "X-Stub-Hit" header — the same pattern <see cref="ProxyRoutingTests"/> uses for the
/// pre-existing auction/search routes. Also covers the two new public/anonymous routes added
/// alongside the admin ones — GET api/banners (new route) and GET api/auctions/duration-limits
/// (already covered by the pre-existing "auctions-read-catchall" route, so no gateway config
/// change was needed for it — see appsettings.json's "banners-read" entry comment).
/// </summary>
[Collection(GatewayServiceApiCollection.Name)]
public class AdminRoutingTests(CustomWebAppFactory factory)
{
    // ── Anonymous caller — 401 problem+json, same shape as AuthenticationTests' ─────────
    [Theory]
    [InlineData("api/admin/users")]
    [InlineData("api/admin/auctions")]
    [InlineData("api/admin/banners")]
    [InlineData("api/admin/settings/duration")]
    [InlineData("api/admin/bids")]
    public async Task AdminRoute_NoToken_Returns401ProblemDetails(string path)
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("X-Stub-Hit"));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(StatusCodes.Status401Unauthorized, doc.RootElement.GetProperty("status").GetInt32());
    }

    // ── Authenticated, non-admin caller — 403 problem+json (RequireRole("admin") fails) ─
    [Theory]
    [InlineData("api/admin/users")]
    [InlineData("api/admin/auctions")]
    [InlineData("api/admin/banners")]
    [InlineData("api/admin/settings/duration")]
    [InlineData("api/admin/bids")]
    public async Task AdminRoute_NonAdminToken_Returns403ProblemDetails(string path)
    {
        var client = factory.CreateClient();
        var token = TestJwt.CreateAccessToken("bob");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("X-Stub-Hit"));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(StatusCodes.Status403Forbidden, doc.RootElement.GetProperty("status").GetInt32());
    }

    // ── Admin caller — routed to the correct resource-owning cluster ────────────────────
    [Theory]
    [InlineData("api/admin/users", "identity-admin-users")]
    [InlineData("api/admin/auctions", "auction-admin-auctions")]
    [InlineData("api/admin/banners", "auction-admin-banners")]
    [InlineData("api/admin/settings/duration", "auction-admin-settings")]
    [InlineData("api/admin/bids", "bidding-admin-bids")]
    public async Task AdminRoute_AdminToken_ReachesExpectedCluster(string path, string expectedStubHit)
    {
        var client = factory.CreateClient();
        var token = TestJwt.CreateAccessToken("admin", roles: ["admin"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedStubHit, response.Headers.GetValues("X-Stub-Hit").Single());
    }

    // Requirements §10.1's per-resource stats endpoint (GET api/admin/users/stats) is a CHILD
    // path, not the base collection path — proving it specifically is what makes the "-catchall"
    // routes' existence load-bearing (a base-path-only route would 404 it at the gateway itself,
    // before ever reaching YARP's forwarder). StubDownstreamServer has no explicit handler for
    // this exact child path, so a 404 here can only mean the request WAS forwarded all the way
    // through the gateway's "admin" AuthorizationPolicy to the identity-cluster destination and
    // 404'd from the stub itself — not that the gateway rejected it beforehand.
    [Fact]
    public async Task AdminUsersStats_AdminToken_MatchesUsersCatchallRoute()
    {
        var client = factory.CreateClient();
        var token = TestJwt.CreateAccessToken("admin", roles: ["admin"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/users/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // The stub has no explicit handler for "/api/admin/users/stats" — it 404s from the
        // STUB itself (proving YARP's "admin-users-catchall" route matched and forwarded the
        // request all the way through the gateway's admin AuthorizationPolicy to the
        // identity-cluster destination, rather than the gateway rejecting it beforehand).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── New public/anonymous routes (Phase 11 Tasks 3.5/3.8) ─────────────────────────────
    [Fact]
    public async Task GetBanners_NoToken_ReachesAuctionStub_Returns200()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/banners", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("banners-get", response.Headers.GetValues("X-Stub-Hit").Single());
    }

    [Fact]
    public async Task GetAuctionsDurationLimits_NoToken_ReachesAuctionStub_Returns200()
    {
        var client = factory.CreateClient();

        // No new gateway route was added for this path — it is already covered by the
        // pre-existing "auctions-read-catchall" entry (appsettings.json).
        using var response = await client.GetAsync("api/auctions/duration-limits", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("auction-duration-limits", response.Headers.GetValues("X-Stub-Hit").Single());
    }
}
