using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityService.Pages.ExternalLogin;

[AllowAnonymous]
[SecurityHeaders]
public class Callback : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly ILogger<Callback> _logger;
    private readonly IEventService _events;
    private readonly ExternalLoginProvisioningService _provisioningService;

    public Callback(
        IIdentityServerInteractionService interaction,
        IEventService events,
        ILogger<Callback> logger,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ExternalLoginProvisioningService provisioningService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _interaction = interaction;
        _logger = logger;
        _events = events;
        _provisioningService = provisioningService;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // read external identity from the temporary cookie
        var result = await HttpContext.AuthenticateAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
        if (result.Succeeded != true)
        {
            throw new InvalidOperationException($"External authentication error: {result.Failure}");
        }

        var externalUser = result.Principal ??
            throw new InvalidOperationException("External authentication produced a null Principal");

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            // Phase 3 Task 15 landmine (b): this call site has existed since the template was
            // scaffolded (Task 1) but was unreachable dead code until this task — no external
            // provider was ever registered, so no external principal ever reached here. Claim
            // TYPES are always safe/useful to see at Debug level (that's the whole point — did
            // the expected claim arrive at all); claim VALUES for email-shaped claim types are
            // redacted (Requirements.md §13.5 — email addresses may not appear in process logs
            // outside the post-sale contact exchange). Other claim values (name, picture link,
            // locale, etc.) are left as-is — Debug is disabled by default in every environment
            // (appsettings.json's LogLevel:Default is "Information"), so this is an
            // explicitly-opted-into diagnostic surface, not one enabled by default.
            var externalClaims = externalUser.Claims.Select(c =>
                c.Type is JwtClaimTypes.Email or ClaimTypes.Email
                    ? $"{c.Type}: (redacted)"
                    : $"{c.Type}: {c.Value}");
            _logger.ExternalClaims(externalClaims);
        }

        // lookup our user and external provider info
        // try to determine the unique id of the external user (issued by the provider)
        // the most common claim type for that are the sub claim and the NameIdentifier
        // depending on the external provider, some other claim type might be used
        var userIdClaim = externalUser.FindFirst(JwtClaimTypes.Subject) ??
                          externalUser.FindFirst(ClaimTypes.NameIdentifier) ??
                          throw new InvalidOperationException("Unknown userid");

        var provider = result.Properties.Items["scheme"] ?? throw new InvalidOperationException("Null scheme in authentication properties");
        var providerUserId = userIdClaim.Value;

        // find external user
        var user = await _userManager.FindByLoginAsync(provider, providerUserId);
        if (user == null)
        {
            // remove the user id and name identifier claims so we don't include it as an extra claim if/when we provision the user
            var claims = externalUser.Claims.ToList();
            _ = claims.RemoveAll(c => c.Type is JwtClaimTypes.Subject or ClaimTypes.NameIdentifier);

            try
            {
                user = await _provisioningService.ProvisionAsync(provider, providerUserId, claims);
            }
            catch (ExternalLoginRejectedException ex)
            {
                // Phase 3 Task 15 landmine (a) — safe rejection, not silent account-linking (see
                // ExternalLoginProvisioningService's remarks). Log the reason CODE and provider
                // scheme only, never any claim value (Requirements.md §13.5). Clean up the
                // temporary external cookie before leaving — the visitor never actually gets
                // signed in, so it shouldn't linger.
                _logger.LogWarning(
                    "External login rejected for provider {Provider}: {Reason}",
                    provider, ex.Reason);
                await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);

                var rejectReturnUrl = result.Properties.Items.TryGetValue("returnUrl", out var ru) ? ru : null;
                return RedirectToPage(
                    "/Account/Login/Index",
                    new { returnUrl = rejectReturnUrl, externalLoginError = ex.Reason });
            }
        }

        // this allows us to collect any additional claims or properties
        // for the specific protocols used and store them in the local auth cookie.
        // this is typically used to store data needed for signout from those protocols.
        var additionalLocalClaims = new List<Claim>();
        var localSignInProps = new AuthenticationProperties();
        CaptureExternalLoginContext(result, additionalLocalClaims, localSignInProps);

        // issue authentication cookie for user
        await _signInManager.SignInWithClaimsAsync(user, localSignInProps, additionalLocalClaims);

        // delete temporary cookie used during external authentication
        await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);

        // retrieve return URL
        var returnUrl = result.Properties.Items["returnUrl"] ?? "~/";

        // check if external login is in the context of an OIDC request
        var context = await _interaction.GetAuthorizationContextAsync(returnUrl, ct);
        await _events.RaiseAsync(new UserLoginSuccessEvent(provider, providerUserId, user.Id, user.UserName, true, context?.Client.ClientId), ct);
        Telemetry.Metrics.UserLogin(context?.Client.ClientId, provider!);

        if (context != null)
        {
            if (context.IsNativeClient())
            {
                // The client is native, so this change in how to
                // return the response is for better UX for the end user.
                return this.LoadingPage(returnUrl);
            }
        }

        return Redirect(returnUrl);
    }

    // if the external login is OIDC-based, there are certain things we need to preserve to make logout work
    // this will be different for WS-Fed, SAML2p or other protocols
    private static void CaptureExternalLoginContext(AuthenticateResult externalResult, List<Claim> localClaims, AuthenticationProperties localSignInProps)
    {
        ArgumentNullException.ThrowIfNull(externalResult.Principal, nameof(externalResult.Principal));

        // capture the idp used to login, so the session knows where the user came from
        localClaims.Add(new Claim(JwtClaimTypes.IdentityProvider, externalResult.Properties?.Items["scheme"] ?? "unknown identity provider"));

        // if the external system sent a session id claim, copy it over
        // so we can use it for single sign-out
        var sid = externalResult.Principal.Claims.FirstOrDefault(x => x.Type == JwtClaimTypes.SessionId);
        if (sid != null)
        {
            localClaims.Add(new Claim(JwtClaimTypes.SessionId, sid.Value));
        }

        // if the external provider issued an id_token, we'll keep it for signout
        var idToken = externalResult.Properties?.GetTokenValue("id_token");
        if (idToken != null)
        {
            localSignInProps.StoreTokens(new[] { new AuthenticationToken { Name = "id_token", Value = idToken } });
        }
    }
}
