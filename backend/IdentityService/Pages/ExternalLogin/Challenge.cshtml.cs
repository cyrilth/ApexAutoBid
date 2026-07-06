using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityService.Pages.ExternalLogin;

[AllowAnonymous]
[SecurityHeaders]
public class Challenge : PageModel
{
    private readonly IIdentityServerInteractionService _interactionService;
    private readonly IAuthenticationSchemeProvider _schemeProvider;

    public Challenge(IIdentityServerInteractionService interactionService, IAuthenticationSchemeProvider schemeProvider)
    {
        _interactionService = interactionService;
        _schemeProvider = schemeProvider;
    }

    public async Task<IActionResult> OnGetAsync(string scheme, string? returnUrl)
    {
        // Phase 3 Task 15 — this page was unreachable dead code before this task (no external
        // scheme was ever registered), so this was never exercised live. Without this check,
        // Challenge(props, scheme) below throws InvalidOperationException for any scheme name
        // that isn't currently registered (verified live: a bare 500, not a 404, when Google's
        // env vars are absent) — a visitor hitting a stale/bookmarked/guessed
        // ?scheme=Google link while Google login is disabled must get a clean 404, not a crash.
        if (await _schemeProvider.GetSchemeAsync(scheme) is null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = "~/";
        }

        // Abort on incorrect returnUrl - it is neither a local url nor a valid OIDC url.
        if (Url.IsLocalUrl(returnUrl) == false && _interactionService.IsValidReturnUrl(returnUrl) == false)
        {
            // user might have clicked on a malicious link - should be logged
            throw new ArgumentException("invalid return URL");
        }

        // start challenge and roundtrip the return URL and scheme
        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Page("/externallogin/callback"),

            Items =
            {
                { "returnUrl", returnUrl },
                { "scheme", scheme },
            }
        };

        return Challenge(props, scheme);
    }
}
