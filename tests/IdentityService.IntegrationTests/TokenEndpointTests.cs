using System.Net;
using System.Text.Json;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Integration tests for the real <c>/connect/token</c> endpoint (Phase 3 Task 11.1).
/// <para>
/// <b>Interpretation note (disclose to the user, as with Task 10):</b> a user-bound token is
/// obtained via a TEST-ONLY Resource Owner Password Credentials client
/// (<see cref="CustomWebAppFactory"/>), not the production `webapp` authorization-code+PKCE
/// client, which is browser-interactive by design (login page, antiforgery token, cookies —
/// exactly right for production, not worth re-driving headlessly through
/// <c>WebApplicationFactory</c>'s in-memory <c>TestServer</c>). The ROPC grant itself is real
/// production code, already registered by <c>.AddAspNetIdentity&lt;ApplicationUser&gt;()</c> in
/// HostingExtensions.cs (verified via decompilation, not assumed) — only the CLIENT allowed to
/// use it is test-only. Zero changes to Config.cs, HostingExtensions.cs, or any appsettings
/// file were made for this task.
/// </para>
/// </summary>
[Collection(IdentityServiceApiCollection.Name)]
public class TokenEndpointTests(CustomWebAppFactory factory)
{
    // ── 11.1  Token endpoint — returns valid JWT with correct claims ─────────────
    //
    // Uses the seeded "admin" user (Requirements.md §8.1) specifically because it's the one
    // seed user guaranteed to carry every claim Task 3 configured, including `role` — bob/
    // alice/tom have no role claim at all, which wouldn't exercise that assertion.
    [Fact]
    public async Task TokenEndpoint_PasswordGrantWithValidCredentials_ReturnsJwtWithConfiguredClaims()
    {
        var client = factory.CreateClient();

        var (accessToken, rawBody) = await TokenClientHelper.RequestPasswordGrantTokenAsync(
            client, "admin", "Pass123$", TestContext.Current.CancellationToken);

        // ── Response shape (application/json, standard OAuth2 token response fields) ────
        using var responseDoc = JsonDocument.Parse(rawBody);
        Assert.Equal("Bearer", responseDoc.RootElement.GetProperty("token_type").GetString());
        Assert.True(responseDoc.RootElement.GetProperty("expires_in").GetInt32() > 0);

        // ── Header: RFC 9068 access-token typ, so it can't be replayed as an id_token and
        //    vice versa (Phase 3 Task 7 hardening on the consumer side relies on this) ──────
        var header = JwtParsingHelper.DecodeHeader(accessToken);
        Assert.Equal("at+jwt", header.GetProperty("typ").GetString());
        Assert.Equal("RS256", header.GetProperty("alg").GetString());

        // ── Payload: exactly the claims Task 3's Config.ApiResources + ProfileService add ──
        var payload = JwtParsingHelper.DecodePayload(accessToken);
        Assert.Equal("apexautobid", payload.GetProperty("aud").GetString());
        Assert.Equal(factory.Server.BaseAddress!.ToString().TrimEnd('/'), payload.GetProperty("iss").GetString());
        Assert.Equal("admin", payload.GetProperty("username").GetString());
        Assert.Equal("admin", payload.GetProperty("role").GetString());
        Assert.True(payload.GetProperty("email_verified").GetBoolean());
        // Structural presence only — never assert/echo the actual email value in output.
        Assert.True(payload.TryGetProperty("email", out var emailClaim));
        Assert.False(string.IsNullOrWhiteSpace(emailClaim.GetString()));
    }

    // Regression guard for the audience-validation wiring (Task 7): a token minted for a scope
    // set that does NOT include "apexautobid" must not carry that audience.
    [Fact]
    public async Task TokenEndpoint_ScopeWithoutApiResource_TokenHasNoApexautobidAudience()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = "admin",
                ["password"] = "Pass123$",
                ["client_id"] = CustomWebAppFactory.TestClientId,
                ["client_secret"] = CustomWebAppFactory.TestClientSecret,
                ["scope"] = "openid profile",
            }),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var payload = JwtParsingHelper.DecodePayload(accessToken);

        Assert.False(payload.TryGetProperty("aud", out _));
    }
}
