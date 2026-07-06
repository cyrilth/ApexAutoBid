using BiddingService.Domain.Entities;
using Contracts;

namespace BiddingService.Application.Services;

/// <summary>
/// Persists an auction's finalized <see cref="Auction.Finished"/> flag and publishes the
/// corresponding <see cref="AuctionFinished"/> event as a single atomic operation (Phase 5
/// Tasks 11/12), so the background finalizer can never mark an auction finished without its
/// event reaching the bus, and a failed publish can never leave the local record silently
/// finalized (or vice versa) — the same "write + publish atomically" guarantee
/// <see cref="IBidPlacementUnitOfWork"/> gives bid placement, applied to auction finalization.
/// </summary>
/// <remarks>
/// Defined in Application (not Domain) for the same reason as <see cref="IBidPlacementUnitOfWork"/>:
/// its contract legitimately mentions <see cref="Contracts.AuctionFinished"/>, and Application
/// already references the <c>Contracts</c> project. The Infrastructure implementation
/// (<c>AuctionFinalizationUnitOfWork</c>) reuses the exact same MassTransit MongoDB
/// transactional "bus outbox" mechanics as <c>BidPlacementUnitOfWork</c> — see that class's
/// remarks for the live-verified transaction semantics, which apply unchanged here.
/// </remarks>
public interface IAuctionFinalizationUnitOfWork
{
    /// <summary>
    /// Atomically finalizes <paramref name="auction"/> — persisting
    /// <see cref="Auction.Finished"/> = <see langword="true"/> — and publishes
    /// <paramref name="finishedEvent"/>, but ONLY if the auction is not already finalized.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if this call actually finalized the auction (and published
    /// <paramref name="finishedEvent"/>); <see langword="false"/> if a concurrent finalization
    /// pass had already finalized it first — a normal, idempotent no-op, not a failure
    /// (phase-end code review Critical 2: double-finalization, and a duplicate
    /// <see cref="AuctionFinished"/> publish, is structurally impossible, not merely unlikely).
    /// </returns>
    Task<bool> FinalizeAsync(Auction auction, AuctionFinished finishedEvent, CancellationToken cancellationToken);
}
