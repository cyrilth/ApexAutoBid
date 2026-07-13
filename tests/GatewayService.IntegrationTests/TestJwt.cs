using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Mints a locally self-signed, structurally faithful access token so
/// <see cref="CustomWebAppFactory"/> can exercise the gateway's REAL <c>JwtBearer</c>
/// authentication handler — including Program.cs's own <c>OnChallenge</c>/<c>OnForbidden</c>
/// ProblemDetails wiring — without a live Duende IdentityServer.
/// </summary>
/// <remarks>
/// This is a different technique from AuctionService/SearchService.IntegrationTests'
/// <c>TestAuthHandler</c>: those swap out authentication entirely for a header-driven custom
/// scheme, which would also remove the gateway's own <c>JwtBearerEvents.OnChallenge</c>/
/// <c>OnForbidden</c> handlers this suite specifically needs to exercise (Task 5.3's
/// gateway-generated 401 problem+json). Instead, <see cref="CustomWebAppFactory"/> keeps the
/// real "Bearer" scheme wired exactly as Program.cs configures it (Authority, ValidAudience,
/// NameClaimType, ValidTypes) and only replaces where its signing keys/issuer come from — a
/// static, in-memory <see cref="OpenIdConnectConfiguration"/> (no discovery-document/JWKS
/// network round-trip) carrying <see cref="SigningKey"/> instead of ones fetched from
/// IdentityServiceUrl. A token minted here therefore has to be a REAL, correctly shaped and
/// signed JWT to pass validation — hence "test-JWT", not "test-auth".
/// </remarks>
internal static class TestJwt
{
    public const string Audience = "apexautobid";

    // HMAC-SHA256 needs at least 256 bits (32 bytes) of key material — this literal is 64
    // bytes, comfortably over that floor. Test-only; never used outside this in-memory host.
    public static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("gateway-integration-tests-only-signing-key-do-not-use-elsewhere-1234567890"));

    /// <summary>
    /// Builds a signed access token carrying the same shape Program.cs's
    /// <c>AddJwtBearer</c> wiring validates: audience "apexautobid", <c>username</c> as the
    /// name claim (<c>NameClaimType = "username"</c>), and an "at+jwt" <c>typ</c> header
    /// (<c>ValidTypes = ["at+jwt"]</c>) — matching the RFC 9068 access-token type Duende itself
    /// issues in production (see AuctionService.API/GatewayService's Program.cs comments).
    /// </summary>
    /// <param name="roles">
    /// Zero or more role names, stamped as short "role" claims — the exact wire claim type
    /// IdentityService's ProfileService uses (JwtClaimTypes.Role = "role"). Omitted (the
    /// default) for ordinary tokens; pass <c>["admin"]</c> to mint an admin token exercising the
    /// gateway's "admin" AuthorizationPolicy (Program.cs's <c>RequireRole("admin")</c>, Phase 11
    /// Task 7) — JwtBearerOptions' default MapInboundClaims=true auto-remaps "role" onto
    /// ClaimTypes.Role before RequireRole/User.IsInRole ever inspect it (see Program.cs's own
    /// "admin" policy comment for the full, decompile-verified reasoning).
    /// </param>
    public static string CreateAccessToken(string username, bool emailVerified = true, IReadOnlyCollection<string>? roles = null)
    {
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);

        var header = new JwtHeader(credentials)
        {
            ["typ"] = "at+jwt",
        };

        var claims = new List<Claim>
        {
            new("username", username),
            // ClaimValueTypes.Boolean matches IdentityService's ProfileService, so the test
            // token carries email_verified as a JSON boolean exactly like a real Duende one.
            new("email_verified", emailVerified ? "true" : "false", ClaimValueTypes.Boolean),
        };
        if (roles is not null)
        {
            claims.AddRange(roles.Select(role => new Claim("role", role)));
        }

        var payload = new JwtPayload(
            issuer: null,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(5));

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
