using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Integration tests for the gateway's edge JWT authentication on a mutating, authenticated
/// route (Docs/Tasks.md Phase 4 Tasks 5.3, 5.4): POST api/auctions is "authenticated" +
/// "strict" per appsettings.json's ReverseProxy:Routes. See <see cref="TestJwt"/>'s remarks for
/// why a locally self-signed token — not <c>TestAuthHandler</c>-style scheme swapping — is used
/// here: these tests specifically exercise Program.cs's real <c>JwtBearerEvents.OnChallenge</c>
/// ProblemDetails conversion, which only runs when the actual "Bearer" scheme is in play.
/// </summary>
[Collection(GatewayServiceApiCollection.Name)]
public class AuthenticationTests(CustomWebAppFactory factory)
{
    // ── 5.3  Mutating route, no token — gateway-generated 401 problem+json ───────
    [Fact]
    public async Task PostAuctions_NoToken_Returns401ProblemDetailsWithTraceId()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsync("api/auctions", content: null, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // RFC 9110 §11.6.1: a 401 must advertise the auth scheme — HandleResponse() in the
        // OnChallenge handler suppresses the default header, so Program.cs rebuilds it. Bare
        // "Bearer" here because no token was presented at all.
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());

        // Gateway-GENERATED, not proxied — the stub never sees this request at all (YARP never
        // forwards it; JwtBearerHandler + the "authenticated" AuthorizationPolicy reject it
        // before MapReverseProxy()'s forwarder runs), so there is no "X-Stub-Hit" header here,
        // unlike every 5.4/5.5 assertion below.
        Assert.False(response.Headers.Contains("X-Stub-Hit"));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(StatusCodes.Status401Unauthorized, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    // ── 5.4  Mutating route, valid token — passes the gateway edge, reaches the stub ──
    [Fact]
    public async Task PostAuctions_ValidToken_ReachesStub_ResponsePassesThrough()
    {
        var client = factory.CreateClient();
        var token = TestJwt.CreateAccessToken("bob");

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auctions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("auction-post", response.Headers.GetValues("X-Stub-Hit").Single());

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("auction-stub", doc.RootElement.GetProperty("source").GetString());
        // Proves YARP forwarded the Authorization header to the downstream destination
        // unchanged, rather than the gateway consuming/stripping it after authenticating.
        Assert.True(doc.RootElement.GetProperty("authorizationHeaderPresent").GetBoolean());
    }

    // Regression guard: garbage bearer tokens must be rejected the same as no token at all.
    [Fact]
    public async Task PostAuctions_GarbageToken_Returns401ProblemDetails()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auctions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // A PRESENTED-but-invalid token additionally gets the OAuth error attributes in
        // WWW-Authenticate (same shape JwtBearerHandler's default challenge produces).
        var wwwAuthenticate = Assert.Single(response.Headers.WwwAuthenticate).ToString();
        Assert.StartsWith("Bearer error=\"invalid_token\"", wwwAuthenticate);
    }
}
