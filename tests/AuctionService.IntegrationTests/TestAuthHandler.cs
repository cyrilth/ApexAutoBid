using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Test-only authentication handler. Reads the username from the <c>X-Test-User</c> request
/// header and produces an authenticated principal with that username, a matching seed-style
/// email, and (Phase 3 Task 19) an <c>email_verified</c> claim controlled by the optional
/// <c>X-Test-EmailVerified</c> header — defaulting to <c>"true"</c> when absent, so every
/// pre-existing test (none of which sets this header) is unaffected. Requests with no
/// <c>X-Test-User</c> header are treated as anonymous (so [Authorize]/[Authorize(Policy=...)]
/// endpoints return 401). This replaces the real JWT bearer scheme in the integration host so
/// tests can act as a specific seeded user, verified or not, without an IdentityServer.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string UserHeader = "X-Test-User";
    public const string EmailVerifiedHeader = "X-Test-EmailVerified";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = values.ToString();

        // Defaults to "true" — matches every pre-existing test's implicit expectation (verified)
        // without them needing to know this header exists at all. Value comparison mirrors the
        // real ProfileService.cs claim exactly (literal lowercase "true"/"false", not parsed as
        // a .NET bool) — Phase 3 Task 19's "EmailVerified" policy does an ordinal string compare
        // against "true", so the test double must produce the same literal shape a real token
        // would to be a faithful stand-in.
        var emailVerified = Request.Headers.TryGetValue(EmailVerifiedHeader, out var evValues)
            ? evValues.ToString()
            : "true";

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, $"{username}@apexautobid.local"),
            new Claim("email_verified", emailVerified),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
