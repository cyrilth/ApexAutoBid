namespace IdentityService.Services;

/// <summary>
/// Validates a Cloudflare Turnstile response token server-side (Phase 3 Task 16.1). Interfaced
/// (rather than a bare concrete class, unlike <c>ExternalLoginProvisioningService</c> — Phase 3
/// Task 15) specifically so Register/Index.cshtml.cs's gating logic can be unit-tested with a
/// substitute, the same pattern already used for <c>IEmailSender&lt;ApplicationUser&gt;</c>.
/// </summary>
public interface ITurnstileValidator
{
    /// <summary>
    /// Calls Cloudflare's <c>siteverify</c> API. Returns <see langword="false"/> for a missing/
    /// empty <paramref name="token"/> (callers should already have short-circuited before this
    /// point to avoid burning a network call — see <see cref="TurnstileValidator"/>'s remarks),
    /// an unsuccessful verification, or any network/deserialization failure (fails CLOSED — see
    /// <see cref="TurnstileValidator"/>'s remarks on why).
    /// </summary>
    Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
