using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using IdentityService.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityService.Pages.Account.Register;

[SecurityHeaders]
[AllowAnonymous]
public class Index : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly ILogger<Index> _logger;

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public Index(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        ILogger<Index> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _interaction = interaction;
        _logger = logger;
    }

    public IActionResult OnGet(string? returnUrl)
    {
        Input = new InputModel { ReturnUrl = returnUrl };
        return Page();
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

        if (!ModelState.IsValid)
        {
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

            return Page();
        }

        _logger.LogInformation("New user registered: {Username}", Input.Username);

        // Email verification (RequireConfirmedEmail) is a later task (Phase 3 Task 14).
        // Until then, unconfirmed accounts are allowed to sign in and browse — the
        // Auction/Bidding Services' email-verified policy already 403s them on mutating
        // actions (create auction / place bid), which is the designed interim state — so
        // signing the new user in immediately here is coherent, not a shortcut around it.
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
