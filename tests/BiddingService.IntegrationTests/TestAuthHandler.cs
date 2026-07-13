using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Test-only authentication handler. Reads the username from the <c>X-Test-User</c> request
/// header and produces an authenticated principal with that username, a matching seed-style
/// email, and an <c>email_verified</c> claim controlled by the optional
/// <c>X-Test-EmailVerified</c> header — defaulting to <c>"true"</c> when absent. Requests with
/// no <c>X-Test-User</c> header are treated as anonymous (so <c>[Authorize]</c> endpoints return
/// 401). Mirrors <c>AuctionService.IntegrationTests.TestAuthHandler</c>/
/// <c>SearchService.IntegrationTests</c>' identical convention verbatim — this replaces the real
/// JWT bearer scheme in the integration host so tests can act as a specific user, verified or
/// not, without an IdentityServer.
/// </summary>
/// <remarks>
/// Phase 11 Task 5.1/5.4 addition: an optional <c>X-Test-Role</c> header stamps a
/// <see cref="ClaimTypes.Role"/> claim so tests can exercise <c>[Authorize(Roles = "admin")]</c>
/// on <c>AdminBidsController</c> — absent by default, so every pre-existing test (none of which
/// sets this header) is unaffected.
/// </remarks>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string UserHeader = "X-Test-User";
    public const string EmailVerifiedHeader = "X-Test-EmailVerified";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = values.ToString();

        // Defaults to "true" so every test that doesn't care about the EmailVerified policy
        // doesn't need to know this header exists. Value comparison mirrors the real
        // ProfileService.cs claim exactly (literal lowercase "true"/"false") — BidsController's
        // "EmailVerified" policy does an ordinal string compare against "true".
        var emailVerified = Request.Headers.TryGetValue(EmailVerifiedHeader, out var evValues)
            ? evValues.ToString()
            : "true";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Email, $"{username}@apexautobid.local"),
            new("email_verified", emailVerified),
        };

        // Absent by default — an authenticated caller with no role claim at all is exactly what
        // [Authorize(Roles = "admin")] must Forbid() (403), same as a real token missing the
        // "role" claim.
        if (Request.Headers.TryGetValue(RoleHeader, out var roleValues) &&
            !string.IsNullOrWhiteSpace(roleValues.ToString()))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleValues.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
