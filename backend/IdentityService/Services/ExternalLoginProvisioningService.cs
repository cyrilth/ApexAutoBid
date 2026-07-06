using System.Security.Claims;
using Duende.IdentityModel;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Services;

/// <summary>
/// Auto-provisions a local <see cref="ApplicationUser"/> for a first-time external-login visitor
/// (Phase 3 Task 15). Extracted out of the duende-is-aspid template's own
/// Pages/ExternalLogin/Callback.cshtml.cs AutoProvisionUserAsync method into a plain,
/// DI-injectable service — matches this project's existing convention of keeping business logic
/// in Services/ (see ProfileService.cs, SmtpEmailSender.cs) — specifically so the provisioning
/// DECISION logic below (duplicate-email rejection, email_verified -&gt; EmailConfirmed) is
/// unit-testable with plain fake claims, without a PageModel/HttpContext harness.
/// </summary>
public class ExternalLoginProvisioningService(UserManager<ApplicationUser> userManager)
{
    public async Task<ApplicationUser> ProvisionAsync(string provider, string providerUserId, IReadOnlyList<Claim> claims)
    {
        var email = claims.FirstOrDefault(x => x.Type == JwtClaimTypes.Email)?.Value ??
                    claims.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value;

        // Phase 3 Task 15 landmine (a). Task 14 set RequireUniqueEmail = true, so a bare
        // CreateAsync below would already fail with DuplicateEmail if a local account already
        // owns this email — but failing isn't the real design question; the real question is
        // what UX that collision should produce. Auto-LINKING this external login to the
        // existing local account was considered and explicitly rejected: that would let anyone
        // who can get Google to assert a given email (a typo-squatted or later-recreated mailbox,
        // for instance) sign straight into whichever local account first registered with that
        // email — a classic account-takeover vector, not something to do silently without proof
        // the same person controls both. The safe default: reject up front and tell the visitor
        // to sign in locally instead — never create, never link.
        if (email is not null)
        {
            var existingLocalUser = await userManager.FindByEmailAsync(email);
            if (existingLocalUser is not null)
            {
                throw new ExternalLoginRejectedException(ExternalLoginRejectedReason.EmailAlreadyRegistered);
            }
        }

        var sub = Guid.NewGuid().ToString();
        var user = new ApplicationUser
        {
            Id = sub,
            // Don't need a real username since the user signs in via the external provider —
            // matches the template's original design. A GUID is, for practical purposes, never
            // going to collide with an existing username (seeded or otherwise); if it somehow
            // did, CreateAsync below would return a DuplicateUserName IdentityResult error, not
            // crash — no bespoke collision-retry loop is warranted for that probability.
            UserName = sub,
        };

        if (email is not null)
        {
            user.Email = email;

            // Requirements.md §3.4: "Google-asserted verified emails are treated as confirmed
            // (no confirmation email sent)". HostingExtensions.cs maps Google's userinfo
            // email_verified (a JSON boolean) to this claim via ClaimActions.MapJsonKey — but
            // JsonKeyClaimAction stores JsonElement.ToString()'s output as the claim Value, which
            // for a JSON bool is empirically "True"/"False" (bool.ToString() capitalization,
            // verified against System.Text.Json directly — NOT the lowercase "true"/"false" JWT
            // convention Services/ProfileService.cs itself emits for this same claim type).
            // bool.TryParse is used specifically because it's documented case-insensitive, so
            // both spellings parse correctly regardless of which provider or JSON shape is
            // involved.
            //
            // If Google ever asserts email_verified=false (or omits the claim/email entirely),
            // the account is left UNCONFIRMED. That is a disclosed residual gap, not silently
            // patched over: Register/Index.cshtml.cs's confirmation-email flow is local-
            // account-registration-only — an external-login user with an unconfirmed email
            // today has no in-app path to trigger a confirmation email themselves.
            var emailVerifiedClaim = claims.FirstOrDefault(x => x.Type == JwtClaimTypes.EmailVerified)?.Value;
            user.EmailConfirmed = bool.TryParse(emailVerifiedClaim, out var verified) && verified;
        }

        // Claims to transfer into the local claims store (display name only — mirrors the
        // template's original scope; unrelated to the security hardening above).
        var filtered = new List<Claim>();
        var name = claims.FirstOrDefault(x => x.Type == JwtClaimTypes.Name)?.Value ??
                   claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
        if (name != null)
        {
            filtered.Add(new Claim(JwtClaimTypes.Name, name));
        }
        else
        {
            var first = claims.FirstOrDefault(x => x.Type == JwtClaimTypes.GivenName)?.Value ??
                        claims.FirstOrDefault(x => x.Type == ClaimTypes.GivenName)?.Value;
            var last = claims.FirstOrDefault(x => x.Type == JwtClaimTypes.FamilyName)?.Value ??
                       claims.FirstOrDefault(x => x.Type == ClaimTypes.Surname)?.Value;
            if (first != null && last != null)
            {
                filtered.Add(new Claim(JwtClaimTypes.Name, first + ' ' + last));
            }
            else if (first != null)
            {
                filtered.Add(new Claim(JwtClaimTypes.Name, first));
            }
            else if (last != null)
            {
                filtered.Add(new Claim(JwtClaimTypes.Name, last));
            }
        }

        var identityResult = await userManager.CreateAsync(user);
        if (!identityResult.Succeeded)
        {
            // Defense-in-depth against a race between the FindByEmailAsync check above and this
            // CreateAsync call (TOCTOU) — treat a DuplicateEmail result the exact same way as the
            // pre-check. Compares against IdentityErrorDescriber's own method name (the literal
            // Code it assigns) rather than a duplicated magic string.
            if (identityResult.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
            {
                throw new ExternalLoginRejectedException(ExternalLoginRejectedReason.EmailAlreadyRegistered);
            }

            // .Code, not .Description — Phase 3 Task 14 landmine (b): DuplicateEmail/InvalidEmail
            // Descriptions embed the raw email, which must never reach process logs/exception
            // messages (Requirements.md §13.5). This throw site (and the two below) previously
            // used .Description, unreviewed since this code path was unreachable before this
            // task (no Google credentials were ever configured).
            throw new InvalidOperationException(
                $"Failed to auto-provision external-login user: {string.Join(", ", identityResult.Errors.Select(e => e.Code))}");
        }

        if (filtered.Count != 0)
        {
            identityResult = await userManager.AddClaimsAsync(user, filtered);
            if (!identityResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to add claims for auto-provisioned user {user.Id}: {string.Join(", ", identityResult.Errors.Select(e => e.Code))}");
            }
        }

        identityResult = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerUserId, provider));
        if (!identityResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to link external login for user {user.Id}: {string.Join(", ", identityResult.Errors.Select(e => e.Code))}");
        }

        return user;
    }
}
