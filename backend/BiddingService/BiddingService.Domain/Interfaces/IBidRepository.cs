using BiddingService.Domain.Entities;

namespace BiddingService.Domain.Interfaces;

/// <summary>
/// Read-side repository abstraction for <see cref="Bid"/> persistence. Defined in Domain so
/// that Application (the bid-placement/read logic) can depend on it without referencing
/// Infrastructure. Domain has zero external NuGet dependencies — only BCL and Domain entity
/// types appear in this interface.
/// </summary>
/// <remarks>
/// The write path used when placing a bid does <b>not</b> go through this interface — see
/// <c>BiddingService.Application.Services.IBidPlacementUnitOfWork</c>, which needs the bid
/// write and the <c>BidPlaced</c> publish to commit atomically (Requirements §3.3 / Task 4).
/// This interface exists purely for reads.
/// </remarks>
public interface IBidRepository
{
    Task<Bid?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every bid for the given auction, newest first (<c>BidTime</c> descending,
    /// <c>Id</c> ascending as a deterministic tiebreaker for bids recorded in the same tick).
    /// </summary>
    Task<List<Bid>> GetByAuctionIdAsync(Guid auctionId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the highest <c>Amount</c> among the auction's <see cref="Enums.BidStatus.Accepted"/>/
    /// <see cref="Enums.BidStatus.AcceptedBelowReserve"/> bids — the only two statuses that
    /// represent a genuine current high bid (mirrors <c>AuctionService</c>/<c>SearchService</c>'s
    /// <c>BidPlacedConsumer</c> semantics exactly) — or <see langword="null"/> when the auction
    /// has no such bid yet.
    /// </summary>
    /// <remarks>
    /// <b>Non-authoritative pre-check only</b> (phase-end code review Critical 1): this read
    /// happens before <c>BidPlacementUnitOfWork</c>'s transaction even starts, so it can be
    /// stale under concurrent bidding on the same auction. It only ever decides the TENTATIVE
    /// status <c>BidAppService.DetermineStatusAsync</c> computes; the actual, race-proof
    /// accept/reject decision is made atomically inside that transaction against the
    /// Infrastructure-only <c>AuctionDocument.CurrentHigh</c> field — see that class's remarks.
    /// Sorted <c>Amount</c> descending, then <c>BidTime</c> ascending (first bidder wins a tie),
    /// then <c>Id</c> — a deterministic tiebreak, mirrored by
    /// <see cref="GetHighestAcceptedBidAsync"/> and the finalizer's winner selection, even though
    /// two Accepted/AcceptedBelowReserve bids for the same auction can never legitimately share
    /// an <c>Amount</c> once the atomic accept path is in place (a strict "&lt;" claim condition
    /// — see <c>BidPlacementUnitOfWork</c>'s remarks — makes an equal-amount second acceptance
    /// impossible going forward); the tiebreak exists purely as a defensive, deterministic
    /// ordering for pre-existing/seeded data that bypassed that path.
    /// </remarks>
    Task<int?> GetHighestAcceptedAmountAsync(Guid auctionId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the auction's highest bid whose status is strictly <see cref="Enums.BidStatus.Accepted"/>
    /// — i.e. it also met the reserve price — or <see langword="null"/> when there is none. Used
    /// only by the background finalizer (Phase 5 Task 12) to determine the sale outcome: unlike
    /// <see cref="GetHighestAcceptedAmountAsync"/> (which also considers
    /// <see cref="Enums.BidStatus.AcceptedBelowReserve"/> — the correct notion of "current high
    /// bid" while an auction is still live, for validating the NEXT bid against), a highest bid
    /// that is only <see cref="Enums.BidStatus.AcceptedBelowReserve"/> means the item did NOT
    /// sell (Requirements §3.3/§8.3 — e.g. seed auction #4, ended with a below-reserve high bid,
    /// is <c>ReserveNotMet</c>, not sold).
    /// </summary>
    /// <remarks>
    /// Sorted <c>Amount</c> descending, then <c>BidTime</c> ascending, then <c>Id</c> — same
    /// deterministic tiebreak as <see cref="GetHighestAcceptedAmountAsync"/>, so the winner the
    /// background finalizer selects is never ambiguous even against pre-existing/seeded data
    /// (see that method's remarks for why a genuine tie can no longer arise going forward).
    /// </remarks>
    Task<Bid?> GetHighestAcceptedBidAsync(Guid auctionId, CancellationToken cancellationToken);
}
