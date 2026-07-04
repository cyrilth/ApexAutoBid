using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Integration tests for a real bearer-protected IdentityService endpoint (Phase 3 Task
/// 11.2/11.3).
/// <para>
/// <b>Interpretation note:</b> <c>/connect/userinfo</c> (the standard OIDC UserInfo endpoint)
/// is used as "the protected endpoint" — it is real, unmodified product surface (part of
/// Duende IdentityServer itself, not a synthetic test-only endpoint), and its whole job is to
/// require and validate a bearer access token. Full claim-content assertions (username, email,
/// email_verified, role, aud, typ) are Task 11.1's job against the token endpoint's own
/// response — these tests only assert the access-control gate: 401 without a token, 200 with
/// a valid one. UserInfo's returned claim set depends on which IDENTITY resources (not API
/// resources) are tied to the token's scopes, which is a separate, unverified concern this
/// task doesn't need to settle — <c>sub</c> is asserted since it's guaranteed for any
/// `openid`-scoped token regardless of that question.
/// </para>
/// </summary>
[Collection(IdentityServiceApiCollection.Name)]
public class ProtectedEndpointTests(CustomWebAppFactory factory)
{
    // ── 11.2  Protected endpoint — rejects request without token ─────────────────
    [Fact]
    public async Task UserInfo_NoBearerToken_Returns401()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/connect/userinfo", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 11.3  Protected endpoint — accepts request with valid token ──────────────
    [Fact]
    public async Task UserInfo_ValidBearerToken_Returns200WithSubjectClaim()
    {
        var client = factory.CreateClient();
        var (accessToken, _) = await TokenClientHelper.RequestPasswordGrantTokenAsync(
            client, "bob", "Pass123$", TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("sub", out var sub));
        Assert.False(string.IsNullOrWhiteSpace(sub.GetString()));
    }

    // Regression guard: an invalid/garbage bearer token must be rejected the same as no token.
    [Fact]
    public async Task UserInfo_GarbageBearerToken_Returns401()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
