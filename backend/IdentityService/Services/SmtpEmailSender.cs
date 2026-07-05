using IdentityService.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityService.Services;

/// <summary>
/// SMTP-backed implementation of ASP.NET Core Identity's <see cref="IEmailSender{TUser}"/>
/// (Phase 3 Task 14.2) — the shared-framework abstraction Identity's own confirmation/
/// password-reset token flows call into (no separate NuGet package needed for the interface
/// itself; it ships in Microsoft.AspNetCore.Identity.dll, decompile-confirmed 2026-07-04).
/// Registered as the app's <see cref="IEmailSender{TUser}"/> in
/// <c>HostingExtensions.ConfigureServices</c>.
/// <para>
/// In development this talks to the Mailpit container (localhost:1025, unauthenticated, no
/// TLS — appsettings.Development.json); in any other environment, <see cref="SmtpOptions"/> is
/// bound from environment variables only (<c>Smtp__Host</c>, <c>Smtp__Username</c>,
/// <c>Smtp__Password</c>, etc.) — never committed (Requirements.md §6).
/// </para>
/// <para>
/// Only ever logs the recipient's stable user id, never the email address itself
/// (Requirements.md §13.5 — email addresses may not appear in process logs outside the
/// post-sale contact exchange).
/// </para>
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    private readonly SmtpOptions _options = options.Value;

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(
            user,
            email,
            "Confirm your ApexAutoBid email address",
            $"""
            <p>Welcome to ApexAutoBid!</p>
            <p>Please confirm your account by <a href="{confirmationLink}">clicking here</a>.</p>
            <p>If you didn't create this account, you can safely ignore this email.</p>
            """);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(
            user,
            email,
            "Reset your ApexAutoBid password",
            $"""
            <p>Reset your password by <a href="{resetLink}">clicking here</a>.</p>
            <p>If you didn't request this, you can safely ignore this email.</p>
            """);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(
            user,
            email,
            "Your ApexAutoBid password reset code",
            $"<p>Your password reset code is: <strong>{resetCode}</strong></p>");

    private async Task SendAsync(ApplicationUser user, string email, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            // StartTls (fail-closed), not StartTlsWhenAvailable: the "WhenAvailable" variant
            // silently proceeds in PLAINTEXT if the server doesn't advertise STARTTLS — which is
            // exactly what an active attacker stripping the STARTTLS capability from the EHLO
            // response produces (decompile-confirmed against MailKit's SecureSocketOptions docs).
            // A production relay that can't do TLS should be a hard connect failure, not a silent
            // downgrade of mail carrying live confirmation/reset tokens.
            var secureSocketOptions = _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions);

            if (!string.IsNullOrEmpty(_options.Username) && !string.IsNullOrEmpty(_options.Password))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);

            logger.LogInformation("Sent email to user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            // Never let a down/misconfigured mail relay break the caller's flow (registration,
            // password reset) — this is a best-effort notification, not the source of truth for
            // email_verified (that's EmailConfirmed, flipped only by a successful
            // ConfirmEmailAsync call against the token). Log the user id only, never the address.
            //
            // The exception OBJECT is deliberately NOT passed to the logger: log formatters
            // render Exception.Message, and MailKit's SmtpCommandException.Message is the
            // server's raw response text (decompile-confirmed), which real relays routinely
            // stuff with the rejected recipient address ("550 5.1.1 <addr>: Recipient address
            // rejected") — that would put the email in process logs (Requirements.md §13.5).
            // Log safe structured fields instead.
            logger.LogWarning(
                "Failed to send email to user {UserId} ({ExceptionType}, SMTP status {SmtpStatusCode})",
                user.Id,
                ex.GetType().Name,
                ex is SmtpCommandException sce ? (int)sce.StatusCode : (object)"n/a");
        }
    }
}
