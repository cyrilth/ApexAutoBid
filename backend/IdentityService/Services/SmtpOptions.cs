using System.ComponentModel.DataAnnotations;

namespace IdentityService.Services;

/// <summary>
/// SMTP settings for <see cref="SmtpEmailSender"/> (Phase 3 Task 14.2). Bound from the "Smtp"
/// configuration section and validated on startup (<c>ValidateOnStart</c> in
/// <c>HostingExtensions.ConfigureServices</c>) so a missing/incomplete configuration fails fast
/// at boot rather than on the first registration attempt.
/// <para>
/// Development values (Mailpit — no credentials, no TLS) live in appsettings.Development.json
/// and are committed by design (Requirements.md §6). Production values are supplied entirely via
/// environment variables (<c>Smtp__Host</c>, <c>Smtp__Username</c>, <c>Smtp__Password</c>, etc.)
/// — appsettings.json only carries non-sensitive defaults/empty placeholders and is never a
/// source of real credentials.
/// </para>
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required(AllowEmptyStrings = false)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    // Mailpit takes unauthenticated SMTP in dev, so these are intentionally optional —
    // SmtpEmailSender only authenticates when both are non-empty.
    public string? Username { get; set; }

    public string? Password { get; set; }

    // Mailpit doesn't speak TLS at all; a real production relay generally does.
    public bool UseSsl { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = "noreply@apexautobid.local";

    [Required(AllowEmptyStrings = false)]
    public string FromName { get; set; } = "ApexAutoBid";
}
