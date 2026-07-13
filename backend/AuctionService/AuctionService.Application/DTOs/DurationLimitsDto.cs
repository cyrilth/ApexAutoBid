namespace AuctionService.Application.DTOs;

/// <summary>
/// The platform's currently-effective auction duration bounds (Phase 11 Task 3.8). Returned
/// by the anonymous <c>GET api/auctions/duration-limits</c> endpoint so the create-auction
/// form can constrain its datepicker, and mirrors the same bounds
/// <c>AuctionAppService</c> validates <c>AuctionEnd</c> against for non-admin callers.
/// </summary>
public class DurationLimitsDto
{
    public required TimeSpan MinDuration { get; init; }
    public required TimeSpan MaxDuration { get; init; }
}
