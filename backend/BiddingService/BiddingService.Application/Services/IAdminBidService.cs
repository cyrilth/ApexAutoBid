using BiddingService.Application.DTOs;

namespace BiddingService.Application.Services;

/// <summary>
/// Result codes for <see cref="IAdminBidService.RemoveBidAsync"/> so the controller can map
/// outcomes to HTTP status codes without the Application layer having any knowledge of HTTP —
/// mirrors <c>BidOutcome</c>'s identical convention.
/// </summary>
public enum RemoveBidOutcome
{
    /// <summary>The bid was removed and the auction's current high bid recalculated.</summary>
    Removed,

    /// <summary>No bid with the given id exists — 404 per Requirements §10.2.</summary>
    NotFound
}

/// <summary>
/// Application-level service for the admin bid-moderation endpoints (Phase 11 Task 5 /
/// Requirements §10.2/§10.4). A distinct interface from <see cref="IBidService"/> — these
/// endpoints (<c>DELETE api/admin/bids/{id}</c>, <c>GET api/admin/bids/stats</c>) are
/// admin-only and have no overlap with the public bid-placement/read surface
/// <see cref="IBidService"/> covers.
/// </summary>
public interface IAdminBidService
{
    /// <summary>
    /// Removes the bid with the given id (deletes it, recalculates the auction's current high
    /// bid from its remaining accepted bids, publishes <c>BidRemoved</c>, and writes an
    /// append-only <c>AuditEntry</c> capturing the removed bid's details) on behalf of
    /// <paramref name="actor"/> (the calling admin's username).
    /// </summary>
    Task<RemoveBidOutcome> RemoveBidAsync(Guid bidId, string actor, CancellationToken cancellationToken);

    /// <summary>Total bid count across every auction (<c>GET api/admin/bids/stats</c>).</summary>
    Task<BidStatsDto> GetStatsAsync(CancellationToken cancellationToken);
}
