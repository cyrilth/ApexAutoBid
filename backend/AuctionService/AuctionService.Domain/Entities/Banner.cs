using AuctionService.Domain.Enums;

namespace AuctionService.Domain.Entities;

/// <summary>
/// An admin-published banner message shown on the home page, a specific auction page, or
/// globally (Requirements §10.3 — Phase 11 Task 3.5).
/// </summary>
public class Banner
{
    public Guid Id { get; set; }
    public required string Message { get; set; }
    public BannerScope Scope { get; set; }

    /// <summary>Required when <see cref="Scope"/> is <see cref="BannerScope.Auction"/>; otherwise null.</summary>
    public Guid? AuctionId { get; set; }

    public DateTime ActiveFrom { get; set; }
    public DateTime ActiveUntil { get; set; }

    /// <summary>The admin username that created (or most recently updated) this banner.</summary>
    public required string CreatedBy { get; set; }
}
