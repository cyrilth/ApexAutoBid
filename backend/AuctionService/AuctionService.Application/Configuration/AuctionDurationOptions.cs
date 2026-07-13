namespace AuctionService.Application.Configuration;

/// <summary>
/// Binds the <c>Auction</c> configuration section — the environment-variable/config layer of
/// the auction-duration resolution order (Requirements §3.1/§10.2 — Phase 11 Task 3.4):
/// DB <c>PlatformSettings</c> (admin-set, takes priority) → this section
/// (<c>Auction__MinDuration</c>/<c>Auction__MaxDuration</c>, TimeSpan format) → the hardcoded
/// defaults below (1 hour – 90 days).
/// <para>
/// Dev/Docker set <c>Auction:MinDuration</c> to <c>00:01:00</c> (1 minute) so a short auction
/// can be created and exercised end-to-end locally.
/// </para>
/// </summary>
public class AuctionDurationOptions
{
    /// <summary>Configuration section name bound from <c>appsettings*.json</c>.</summary>
    public const string SectionName = "Auction";

    /// <summary>Minimum allowed auction duration. Defaults to 1 hour.</summary>
    public TimeSpan MinDuration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Maximum allowed auction duration. Defaults to 90 days.</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromDays(90);
}
