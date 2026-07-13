using BiddingService.Domain.Entities;
using Contracts;

namespace BiddingService.Application.Services;

/// <summary>
/// Atomically removes a bid, recalculates the auction's current high bid from the remaining
/// accepted bids, publishes <see cref="BidRemoved"/>, and writes the admin's <see cref="AuditEntry"/>
/// — all as a single operation (Phase 11 Task 5.1/5.5). Mirrors <c>IBidPlacementUnitOfWork</c>'s
/// "write + publish atomically" convention, extended to also cover the audit write in the same
/// scope (Requirements §13.3: "for MongoDB, the same operation scope — best effort").
/// </summary>
/// <remarks>
/// Defined in Application (not Domain) for the same reason as <c>IBidPlacementUnitOfWork</c>/
/// <c>IAuctionFinalizationUnitOfWork</c>: its contract legitimately mentions
/// <see cref="Contracts.BidRemoved"/>, and Application already references the <c>Contracts</c>
/// project. The Infrastructure implementation (<c>BidRemovalUnitOfWork</c>) reuses the same
/// MassTransit MongoDB transactional "bus outbox" mechanics as <c>BidPlacementUnitOfWork</c>/
/// <c>AuctionFinalizationUnitOfWork</c>.
/// </remarks>
public interface IBidRemovalUnitOfWork
{
    /// <summary>
    /// Deletes <paramref name="bid"/>, recomputes the auction's current high bid from its
    /// remaining <c>Accepted</c>/<c>AcceptedBelowReserve</c> bids (<see langword="null"/> when
    /// none remain), publishes <c>BidRemoved(BidId, AuctionId, CurrentHighBid?)</c>, and inserts
    /// <paramref name="auditEntry"/> — atomically.
    /// </summary>
    /// <returns>The recalculated current high bid amount, or <see langword="null"/> if the
    /// auction has no remaining accepted bid.</returns>
    Task<int?> RemoveAsync(Bid bid, AuditEntry auditEntry, CancellationToken cancellationToken);
}
