namespace AuctionService.Application.DTOs;

/// <summary>
/// Read DTO for <c>GET api/admin/auctions/stats</c> (Phase 11 Task 3.7). <see cref="ByStatus"/>
/// always contains an entry for every <c>Status</c> enum value (0 when there are none),
/// keyed by the enum's string name (e.g. "Live", "Finished", "ReserveNotMet", "Cancelled") —
/// the same string convention <see cref="AuctionDto.Status"/> uses.
/// </summary>
public class AuctionStatsDto
{
    public required int Total { get; init; }
    public required Dictionary<string, int> ByStatus { get; init; }
}
