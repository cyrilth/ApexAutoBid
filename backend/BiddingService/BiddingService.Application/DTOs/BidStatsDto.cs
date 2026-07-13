namespace BiddingService.Application.DTOs;

/// <summary>
/// Read DTO for <c>GET api/admin/bids/stats</c> (Phase 11 Task 5.4 / Requirements §10.4) — total
/// bid count across every auction and every <c>BidStatus</c>.
/// </summary>
public class BidStatsDto
{
    public required long TotalBids { get; init; }
}
