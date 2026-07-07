using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Test-only authentication handler that replaces the real JwtBearer scheme in the integration
/// host, mirroring <c>BiddingService.IntegrationTests.TestAuthHandler</c>'s convention — but with
/// a twist specific to this service and to the <see cref="Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling"/>
/// transport <c>SignalRTestHelpers.CreateConnection</c> forces.
/// </summary>
/// <remarks>
/// <para>
/// Program.cs's real JwtBearer handler pulls the token out of the <c>access_token</c>
/// query-string parameter (not the Authorization header — see its own
/// <c>OnMessageReceived</c> remarks), because browsers can't set custom headers on a WebSocket/
/// Server-Sent-Events handshake. <b>That is specific to those two transports</b>: the SignalR
/// .NET client's own <c>AccessTokenProvider</c> behavior (confirmed empirically while building
/// this suite) instead sends the token as a normal <c>Authorization: Bearer &lt;token&gt;</c>
/// header for the <see cref="Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling"/>
/// transport used here — never the query string. Since there is no real IdentityServer in this
/// suite to issue/validate a JWT against, this handler reads whichever of the two carries a
/// value (the header for every connection this suite actually makes; the query string kept as a
/// fallback purely for documentation/robustness) and treats it AS the username directly, rather
/// than as an encoded token to decode. That is exactly what
/// <c>SignalRTestHelpers.CreateConnection</c>'s <c>AccessTokenProvider</c> supplies: the desired
/// test username, verbatim.
/// </para>
/// <para>
/// The resulting <see cref="ClaimsIdentity"/> carries that username under the <c>"username"</c>
/// claim type (not <see cref="ClaimTypes.Name"/>) and is constructed with
/// <c>nameType: "username"</c>, mirroring Program.cs's real
/// <c>TokenValidationParameters.NameClaimType = "username"</c> — so
/// <c>HubConnectionContext.User.Identity.Name</c> resolves to the username exactly as it does in
/// production, which is what <see cref="NotificationService.Hubs.UsernameUserIdProvider"/> reads
/// to power <c>Clients.User(...)</c> targeted sends.
/// </para>
/// <para>
/// A request with neither an <c>access_token</c> query parameter nor a Bearer Authorization
/// header authenticates as anonymous (no result), matching the real hub's "authentication is
/// additive, not required" behavior (Task 3.1) — such a connection still reaches
/// <c>NotificationHub</c> (it carries no <c>[Authorize]</c>) and receives <c>Clients.All</c>
/// broadcasts only.
/// </para>
/// </remarks>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var username = ExtractUsername();

        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[] { new Claim("username", username) };
        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "username", roleType: ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ExtractUsername()
    {
        // See class remarks: LongPolling — the only transport this suite ever uses — carries the
        // token as a normal Bearer Authorization header, never the access_token query string.
        if (AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header) &&
            string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(header.Parameter))
        {
            return header.Parameter;
        }

        // Fallback for the query-string convention Program.cs's real JwtBearer handler uses
        // (WebSockets/SSE) — never actually exercised by this suite's LongPolling connections,
        // kept only so this handler behaves correctly if a future test switches transports.
        var queryToken = Request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(queryToken) ? null : queryToken;
    }
}
