using System.ComponentModel.DataAnnotations;

namespace IdentityService.Services;

/// <summary>
/// Cloudflare Turnstile settings for <see cref="TurnstileValidator"/> (Phase 3 Task 16.1/16.2).
/// Bound from the "Turnstile" configuration section and validated on startup
/// (<c>ValidateOnStart</c> in <c>HostingExtensions.ConfigureServices</c>), the same fail-fast
/// shape as <c>SmtpOptions</c> (Phase 3 Task 14) — a missing/incomplete configuration fails at
/// boot, not on the first registration attempt.
/// <para>
/// <see cref="SiteKey"/> is not a secret — Cloudflare's own docs make clear it's meant to be
/// embedded directly in the page's HTML (<c>data-sitekey</c>). <see cref="SecretKey"/> IS a
/// secret and is only ever sent server-side to Cloudflare's siteverify endpoint, never rendered
/// or logged.
/// </para>
/// <para>
/// Dev/Docker values (appsettings.Development.json) are Cloudflare's own official
/// always-pass test keys — published by Cloudflare specifically to be safe to commit and use
/// in dev/CI/e2e without any real account (Requirements.md §3.4/CLAUDE.md §6). Production keys
/// are real external credentials supplied via environment variables only, exactly like the
/// Google OAuth credentials (Phase 3 Task 15) — appsettings.json carries empty placeholders,
/// never a real or test value.
/// </para>
/// </summary>
public class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    [Required(AllowEmptyStrings = false)]
    public string SiteKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}
