using AuctionService.Application.DTOs;

namespace AuctionService.Application.Services;

/// <summary>Result codes for <see cref="IPlatformSettingsService.UpdateDurationSettingsAsync"/>.</summary>
public enum PlatformSettingsWriteResult
{
    Success,

    /// <summary>
    /// <c>MinDuration</c> was not strictly positive, or <c>MaxDuration</c> did not strictly
    /// exceed <c>MinDuration</c>.
    /// </summary>
    InvalidRange
}

/// <summary>
/// Result of <see cref="IPlatformSettingsService.UpdateDurationSettingsAsync"/>.
/// <see cref="Settings"/> is non-null only when <see cref="Status"/> is
/// <see cref="PlatformSettingsWriteResult.Success"/>.
/// </summary>
public record PlatformSettingsUpdateResult(PlatformSettingsWriteResult Status, PlatformSettingsDto? Settings);

/// <summary>
/// Application-level service for the platform-wide auction duration bounds (Requirements
/// §3.1/§10.2 — Phase 11 Task 3.8). Resolution order for the EFFECTIVE bounds: the single
/// DB-stored <c>PlatformSettings</c> row (admin-set, read fresh on every call — no caching, so
/// a PUT takes effect immediately with no restart) → <c>Auction:MinDuration</c>/
/// <c>Auction:MaxDuration</c> configuration/environment variables → hardcoded defaults
/// (1 hour – 90 days).
/// </summary>
public interface IPlatformSettingsService
{
    /// <summary>
    /// Resolves the effective Min/Max duration bounds right now. Used both by
    /// <c>AuctionAppService</c>'s non-admin <c>AuctionEnd</c> validation and by
    /// <see cref="AuctionService.Application.Services.IAuctionService.GetDurationLimitsAsync"/>.
    /// </summary>
    Task<(TimeSpan MinDuration, TimeSpan MaxDuration)> GetEffectiveDurationBoundsAsync();

    /// <summary>
    /// Backs <c>GET api/admin/settings/duration</c>. Returns the current DB override if one
    /// has ever been saved (with its <c>UpdatedBy</c>/<c>UpdatedAt</c>), otherwise the
    /// configuration/environment defaults with both stamped as <see langword="null"/>.
    /// </summary>
    Task<PlatformSettingsDto> GetDurationSettingsAsync();

    /// <summary>
    /// Backs <c>PUT api/admin/settings/duration</c>. Upserts the single settings row and
    /// writes an append-only <c>AuditEntry</c> ("PlatformSettingsUpdated") in the same
    /// <c>SaveChanges</c> as the mutation (Requirements §13.3). Takes effect immediately —
    /// every subsequent <see cref="GetEffectiveDurationBoundsAsync"/> call re-reads the DB.
    /// </summary>
    Task<PlatformSettingsUpdateResult> UpdateDurationSettingsAsync(UpdateDurationSettingsDto dto, string updatedBy);
}
