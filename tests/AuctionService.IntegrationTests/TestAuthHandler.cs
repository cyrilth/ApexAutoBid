using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Test-only authentication handler. Reads the username from the <c>X-Test-User</c> request
/// header and produces an authenticated principal with that username, a matching seed-style
/// email, and <c>email_verified=true</c>. Requests with no header are treated as anonymous
/// (so [Authorize] endpoints return 401). This replaces the real JWT bearer scheme in the
/// integration host so tests can act as a specific seeded user without an IdentityServer.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = values.ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, $"{username}@apexautobid.local"),
            new Claim("email_verified", "true"),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
