namespace AuctionService.Domain.Entities;

/// <summary>
/// Admin-editable platform-wide auction duration bounds (Requirements §10.2 — Phase 11 Task
/// 3.8). At most one row ever exists; when absent, the effective bounds fall back to
/// configuration/environment variables and finally to hardcoded defaults (see
/// <c>AuctionService.Application.Services.IPlatformSettingsService</c>).
/// </summary>
public class PlatformSettings
{
    public Guid Id { get; set; }
    public TimeSpan MinDuration { get; set; }
    public TimeSpan MaxDuration { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
