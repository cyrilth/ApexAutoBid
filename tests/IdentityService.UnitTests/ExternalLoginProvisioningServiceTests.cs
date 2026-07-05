using System.Security.Claims;
using Duende.IdentityModel;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for the provisioning DECISION logic extracted out of
/// Pages/ExternalLogin/Callback.cshtml.cs (Phase 3 Task 15) — exactly the coverage the task
/// asked for: a verified-email new user is auto-confirmed, a duplicate email is safely rejected
/// (never silently linked), and an unverified (or absent) email_verified claim leaves the
/// account unconfirmed. Uses the same REAL <see cref="UserManager{TUser}"/>-backed-by-
/// <see cref="InMemoryUserStore"/> approach as RegisterPageTests/LoginPageTests (see
/// <see cref="TestIdentityFactory"/>'s remarks) so RequireUniqueEmail/FindByEmailAsync run for
/// real, not a canned double.
/// </summary>
public class ExternalLoginProvisioningServiceTests
{
    private readonly InMemoryUserStore _store = new();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ExternalLoginProvisioningService _sut;

    public ExternalLoginProvisioningServiceTests()
    {
        _userManager = TestIdentityFactory.CreateUserManager(_store);
        _sut = new ExternalLoginProvisioningService(_userManager);
    }

    // ── 15 — new user, Google-verified email -> EmailConfirmed = true ────────────
    [Fact]
    public async Task ProvisionAsync_NewUserWithVerifiedEmail_CreatesConfirmedUserAndLinksLogin()
    {
        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Email, "newgoogleuser@apexautobid.local"),
            // Empirically "True"/"False" (bool.ToString() capitalization), not lowercase — see
            // ExternalLoginProvisioningService's remarks for why this exact casing is asserted.
            new(JwtClaimTypes.EmailVerified, "True"),
            new(JwtClaimTypes.Name, "New Google User"),
        };

        var user = await _sut.ProvisionAsync("Google", "google-sub-123", claims);

        Assert.Equal("newgoogleuser@apexautobid.local", user.Email);
        Assert.True(user.EmailConfirmed);

        var stored = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.EmailConfirmed);

        var logins = await _userManager.GetLoginsAsync(user);
        Assert.Contains(logins, l => l.LoginProvider == "Google" && l.ProviderKey == "google-sub-123");
    }

    // ── 15 — email_verified = false (or absent) -> left unconfirmed ──────────────
    [Fact]
    public async Task ProvisionAsync_UnverifiedEmail_CreatesUnconfirmedUser()
    {
        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Email, "unverifiedgoogleuser@apexautobid.local"),
            new(JwtClaimTypes.EmailVerified, "False"),
        };

        var user = await _sut.ProvisionAsync("Google", "google-sub-456", claims);

        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task ProvisionAsync_MissingEmailVerifiedClaim_CreatesUnconfirmedUser()
    {
        // No email_verified claim at all — a defensively-coded provider omission, not just the
        // "false" case above.
        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Email, "noverifiedclaim@apexautobid.local"),
        };

        var user = await _sut.ProvisionAsync("Google", "google-sub-789", claims);

        Assert.False(user.EmailConfirmed);
    }

    // ── 15 landmine (a) — duplicate email is rejected, never auto-linked ─────────
    [Fact]
    public async Task ProvisionAsync_EmailAlreadyRegisteredLocally_ThrowsRejectedExceptionAndCreatesNoUser()
    {
        var existingLocalUser = new ApplicationUser { UserName = "localowner", Email = "shared@apexautobid.local" };
        var seedResult = await _userManager.CreateAsync(existingLocalUser, "Pass123$");
        Assert.True(seedResult.Succeeded, string.Join(", ", seedResult.Errors.Select(e => e.Code)));

        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Email, "shared@apexautobid.local"),
            new(JwtClaimTypes.EmailVerified, "True"),
        };

        var ex = await Assert.ThrowsAsync<ExternalLoginRejectedException>(
            () => _sut.ProvisionAsync("Google", "google-sub-attacker", claims));

        Assert.Equal(ExternalLoginRejectedReason.EmailAlreadyRegistered, ex.Reason);

        // No external login was linked to the existing local account — critically, NOT an
        // auto-link (that's the account-takeover vector this design explicitly avoids).
        var logins = await _userManager.GetLoginsAsync(existingLocalUser);
        Assert.Empty(logins);
    }
}
