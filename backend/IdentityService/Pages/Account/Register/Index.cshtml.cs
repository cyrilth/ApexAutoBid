using System.Text;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using IdentityService.Pages;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace IdentityService.Pages.Account.Register;

// Phase 3 Task 16.1 — ExtraScriptSrc/ExtraFrameSrc scope the CSP loosening to exactly this page
// (see SecurityHeadersAttribute's remarks); every other [SecurityHeaders] usage is untouched.
[SecurityHeaders(ExtraScriptSrc = "https://challenges.cloudflare.com", ExtraFrameSrc = "https://challenges.cloudflare.com")]
[AllowAnonymous]
public class Index : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEmailSender<ApplicationUser> _emailSender;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly ITurnstileValidator _turnstileValidator;
    private readonly TurnstileOptions _turnstileOptions;
    private readonly ILogger<Index> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    // Phase 3 Task 16.1 — Cloudflare's widget script injects a hidden <input> with this EXACT
    // (hyphenated, non-nested) name; it cannot be renamed, and a C# property can't contain
    // hyphens, hence the explicit [BindProperty(Name = ...)] rather than folding it into
    // InputModel like every other field.
    [BindProperty(Name = "cf-turnstile-response")]
    public string? TurnstileResponse { get; set; }

    // The Turnstile SITE key is not a secret — Cloudflare's own docs say it's meant to be
    // embedded directly in the page's HTML (data-sitekey). Only SecretKey (never exposed here)
    // is sensitive.
    public string TurnstileSiteKey => _turnstileOptions.SiteKey;

    // Phase 3 Task 15.2 — "Sign in with Google" on the login AND register pages. Deliberately a
    // plain IReadOnlyList<AuthenticationScheme> rather than reusing
    // Pages/Account/Login/ViewModel.ExternalProvider: Register doesn't need Login's fuller
    // IdP-restriction/OIDC-context-shortcut logic (BuildModelAsync's context.IdP branch) — it
    // only ever needs "which schemes are registered right now", so the framework's own type is
    // enough and avoids coupling Register to a Login-page-specific view model.
    public IReadOnlyList<AuthenticationScheme> ExternalProviders { get; private set; } = [];

    public Index(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IEmailSender<ApplicationUser> emailSender,
        IAuthenticationSchemeProvider schemeProvider,
        ITurnstileValidator turnstileValidator,
        IOptions<TurnstileOptions> turnstileOptions,
        ILogger<Index> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _interaction = interaction;
        _emailSender = emailSender;
        _schemeProvider = schemeProvider;
        _turnstileValidator = turnstileValidator;
        _turnstileOptions = turnstileOptions.Value;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl)
    {
        Input = new InputModel { ReturnUrl = returnUrl };
        await PopulateExternalProvidersAsync();
        return Page();
    }

    private async Task PopulateExternalProvidersAsync()
    {
        var schemes = await _schemeProvider.GetAllSchemesAsync();
        ExternalProviders = schemes.Where(s => s.DisplayName is not null).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Mirrors Login/Index.cshtml.cs's cancel handling: if the user backs out, deny the
        // pending authorize request (if any) rather than leaving it dangling, else go home.
        if (Input.Button != "register")
        {
            var cancelContext = await _interaction.GetAuthorizationContextAsync(Input.ReturnUrl, ct);
            if (cancelContext != null)
            {
                ArgumentNullException.ThrowIfNull(Input.ReturnUrl, nameof(Input.ReturnUrl));
                await _interaction.DenyAuthorizationAsync(cancelContext, InteractionError.AccessDenied, ct);

                if (cancelContext.IsNativeClient())
                {
                    return this.LoadingPage(Input.ReturnUrl);
                }

                return Redirect(Input.ReturnUrl ?? "~/");
            }

            return Redirect("~/");
        }

        // Phase 3 Task 16.1 — Cloudflare Turnstile bot protection. Rejects EARLY without calling
        // Cloudflare at all when the field is missing/empty (Requirements.md §3.4's explicit
        // instruction — don't burn a siteverify call on an obviously-incomplete submission,
        // e.g. JS disabled or a direct POST bypassing the widget entirely). Never logs the
        // token value itself, only that the check failed (§13.5).
        if (string.IsNullOrWhiteSpace(TurnstileResponse))
        {
            _logger.LogWarning("Registration rejected — missing Turnstile response token");
            ModelState.AddModelError(string.Empty, "Please complete the verification challenge.");
        }
        else if (!await _turnstileValidator.ValidateAsync(TurnstileResponse, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
        {
            _logger.LogWarning("Registration rejected — Turnstile validation failed");
            ModelState.AddModelError(string.Empty, "Verification challenge failed. Please try again.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateExternalProvidersAsync();
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = Input.Username,
            Email = Input.Email,
        };

        var result = await _userManager.CreateAsync(user, Input.Password!);
        if (!result.Succeeded)
        {
            // UserManager surfaces its own validation (duplicate username, password policy,
            // invalid email format, etc.) as IdentityResult.Errors — never a 500.
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            _logger.LogWarning(
                "Registration failed for username {Username} with {ErrorCount} validation error(s)",
                Input.Username, result.Errors.Count());

            await PopulateExternalProvidersAsync();
            return Page();
        }

        _logger.LogInformation("New user registered: {Username}", Input.Username);

        // Phase 3 Task 14.1 — send the confirmation email. Token is base64url-encoded before
        // going in the query string (the raw DataProtector token can contain '+'/'/' — the same
        // convention ASP.NET Core Identity's own scaffolded UI pages use). The link is built
        // directly from Request.Scheme/Host rather than Url.Page(...): ConfirmEmail.cshtml is a
        // fixed, hardcoded route (no area/versioning to resolve), and Url.Page(...) needs a
        // fully-populated IUrlHelper.ActionContext.RouteData (decompile-confirmed via
        // UrlHelperExtensions.Page) that only a live request pipeline provides — reading the
        // scheme/host directly off the actual incoming request is simpler, unit-testable with a
        // plain DefaultHttpContext, and correct in every environment this runs in (dev, and any
        // future containerized deployment) without duplicating an "IdentityServiceUrl" setting.
        //
        // Trusting Request.Host here is only safe because AllowedHosts (appsettings.json) is
        // restricted — a forged Host header would otherwise poison this link, sending the live
        // confirmation token to an attacker-chosen domain. HostFilteringMiddleware (wired by the
        // framework's web defaults) rejects non-matching Host headers with a 400 before this
        // code runs. Any deployment serving a new hostname must extend AllowedHosts (env var
        // ALLOWEDHOSTS or config override), or registration/link-building breaks with 400s.
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationLink =
            $"{Request.Scheme}://{Request.Host}/Account/ConfirmEmail" +
            $"?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedToken)}";

        try
        {
            await _emailSender.SendConfirmationLinkAsync(user, user.Email!, confirmationLink);
            _logger.LogInformation("Confirmation email queued for user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            // SmtpEmailSender itself already catches send failures and never throws — this is
            // defense-in-depth against a future implementation swap. Either way, a mail-relay
            // outage must never block registration/sign-in: the user isn't blocked from signing
            // in without confirming (Requirements.md §3.4), so a lost email is recoverable
            // (a future "resend confirmation" action), not fatal to this request.
            // Like SmtpEmailSender's own catch, the exception object is not passed to the
            // logger — an SMTP exception's message can embed the recipient address (§13.5).
            _logger.LogWarning(
                "Failed to queue confirmation email for user {UserId} ({ExceptionType})",
                user.Id, ex.GetType().Name);
        }

        // Unconfirmed accounts CAN sign in and browse (Requirements.md §3.4) — only mutating
        // actions (create auction / place bid) require email_verified, enforced by the
        // Auction/Bidding Services (Phase 3 Task 14.4, already in place — see
        // AuctionsController.cs). Signing the new user in immediately here is therefore still
        // the coherent behavior, unchanged from before this task, now paired with an actual
        // confirmation email instead of a comment promising one.
        await _signInManager.SignInAsync(user, isPersistent: false);

        var context = await _interaction.GetAuthorizationContextAsync(Input.ReturnUrl, ct);
        if (context != null)
        {
            ArgumentNullException.ThrowIfNull(Input.ReturnUrl, nameof(Input.ReturnUrl));

            if (context.IsNativeClient())
            {
                return this.LoadingPage(Input.ReturnUrl);
            }

            return Redirect(Input.ReturnUrl);
        }

        if (Url.IsLocalUrl(Input.ReturnUrl))
        {
            return Redirect(Input.ReturnUrl);
        }

        if (string.IsNullOrEmpty(Input.ReturnUrl))
        {
            return Redirect("~/");
        }

        // user might have clicked on a malicious link - should be logged
        throw new ArgumentException("invalid return URL");
    }
}
