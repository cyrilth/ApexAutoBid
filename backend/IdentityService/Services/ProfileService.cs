using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Services;

/// <summary>
/// Supplies the ApexAutoBid-specific claims (Requirements.md §3.4: username, email, role —
/// plus email_verified, required by AuctionService.API's existing email-verified policy) that
/// ASP.NET Identity's default profile service (wired up by .AddAspNetIdentity&lt;ApplicationUser&gt;()
/// in HostingExtensions) does not populate on its own. Registered via
/// .AddProfileService&lt;ProfileService&gt;() AFTER .AddAspNetIdentity(...) so this
/// implementation replaces the default one for both the OIDC userinfo endpoint and access
/// tokens (Phase 3 Task 3).
/// </summary>
public class ProfileService(UserManager<ApplicationUser> userManager) : IProfileService
{
    public async Task GetProfileDataAsync(ProfileDataRequestContext context, CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(context.Subject);
        if (user is null)
        {
            return;
        }

        var roles = await userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(Config.UsernameClaimType, user.UserName ?? string.Empty),
            new(JwtClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtClaimTypes.EmailVerified, user.EmailConfirmed ? "true" : "false", ClaimValueTypes.Boolean),
        };
        claims.AddRange(roles.Select(role => new Claim(JwtClaimTypes.Role, role)));

        // Only issue the claims Duende actually asked for — RequestedClaimTypes is populated
        // from the UserClaims declared on whichever resources/scopes the current request's
        // client and token type include (see Config.ApiResources).
        context.IssuedClaims.AddRange(claims.Where(c => context.RequestedClaimTypes.Contains(c.Type)));
    }

    public async Task IsActiveAsync(IsActiveContext context, CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(context.Subject);
        context.IsActive = user is not null;
    }
}
