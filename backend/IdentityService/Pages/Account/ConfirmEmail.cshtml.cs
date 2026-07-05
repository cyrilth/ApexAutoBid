using System.Text;
using IdentityService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace IdentityService.Pages.Account;

/// <summary>
/// Landing page for the confirmation link sent by Register/Index.cshtml.cs (Phase 3 Task 14.1).
/// Not scaffolded by the duende-is-aspid template — Pages/Account only had AccessDenied/Login/
/// Logout/Register before this task (confirmed by directory listing).
/// <para>
/// [AllowAnonymous] is required: the link is opened from an email client, possibly in a
/// different browser/session than the one that registered, so the visitor may not be
/// authenticated. MapRazorPages() in HostingExtensions.ConfigurePipeline applies
/// RequireAuthorization() to every page by default.
/// </para>
/// </summary>
[SecurityHeaders]
[AllowAnonymous]
public class ConfirmEmailModel(UserManager<ApplicationUser> userManager, ILogger<ConfirmEmailModel> logger) : PageModel
{
    public bool Succeeded { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(string? userId, string? code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            ErrorMessage = "The confirmation link is invalid.";
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            // Same generic message whether the user id doesn't exist or the token is bad —
            // don't reveal which, to a visitor who may not be who they claim.
            ErrorMessage = "The confirmation link is invalid or has expired.";
            return;
        }

        if (user.EmailConfirmed)
        {
            // Link re-clicked (e.g. from a mail client's link-prefetching) or the user
            // confirmed already via another tab — treat as success, not an error.
            Succeeded = true;
            logger.LogInformation("Email confirmation link re-used for already-confirmed user {UserId}", user.Id);
            return;
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            ErrorMessage = "The confirmation link is invalid.";
            return;
        }

        var result = await userManager.ConfirmEmailAsync(user, decodedToken);
        if (result.Succeeded)
        {
            Succeeded = true;
            logger.LogInformation("Email confirmed for user {UserId}", user.Id);
        }
        else
        {
            ErrorMessage = "The confirmation link is invalid or has expired.";

            // Error codes only, never Descriptions — see SeedData.cs's Task 14 landmine (b)
            // comment; ConfirmEmailAsync's own errors ("InvalidToken") don't carry an email
            // today, but logging codes uniformly avoids relying on that staying true.
            logger.LogWarning(
                "Email confirmation failed for user {UserId} with error(s) {ErrorCodes}",
                user.Id, string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
