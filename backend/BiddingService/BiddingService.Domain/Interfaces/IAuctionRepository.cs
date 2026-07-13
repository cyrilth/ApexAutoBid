using BiddingService.Domain.Entities;

namespace BiddingService.Domain.Interfaces;

/// <summary>
/// Persistence abstraction for this service's own local <see cref="Auction"/> projection
/// (Architecture.md §4.2) — <b>not</b> Auction Service's own repository; a same-named
/// interface in a different assembly with an entirely different (much smaller) entity shape.
/// Defined in Domain so Application can depend on it without referencing Infrastructure.
/// </summary>
public interface IAuctionRepository
{
    Task<Auction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new local auction record. A no-op — never overwrites anything — when a record
    /// with the same id already exists.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately insert-only, not a whole-document upsert/replace</b> (phase-end code
    /// review Warning 3): <c>AuctionCreated</c> describes the auction's CREATION, so redelivery
    /// (or a late/duplicate consume) must be a genuine no-op once a local record already exists
    /// — a whole-document replace previously reset <see cref="Auction.Finished"/> back to
    /// <see langword="false"/> on every replay, silently un-finalizing an already-finished
    /// auction and causing a second, spurious <c>AuctionFinished</c> publish once the
    /// background finalizer next ran, and would equally have clobbered the Mongo-only
    /// <c>AuctionDocument.CurrentHigh</c> field (Critical 1) back to its default. Still
    /// idempotent — just via "first write wins" rather than "last write wins".
    /// </remarks>
    Task InsertIfNotExistsAsync(Auction auction, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every local auction that has passed <see cref="Auction.AuctionEnd"/> (as of
    /// <paramref name="asOf"/>) but is not yet <see cref="Auction.Finished"/> — the background
    /// finalizer's (Phase 5 Task 12) candidate set each tick. Excludes auctions already marked
    /// <see cref="Auction.Finished"/>, which is exactly what makes finalization idempotent: an
    /// auction can never be selected twice once <c>IAuctionFinalizationUnitOfWork.FinalizeAsync</c>
    /// commits.
    /// </summary>
    /// <remarks>
    /// <b>Grace period (phase-end code review Critical 2):</b> this method's own filter shape
    /// stays a bare <c>AuctionEnd &lt;= asOf</c> — it is <c>AuctionFinalizationAppService</c>
    /// that computes <paramref name="asOf"/> as <c>now - Bidding:FinalizationGraceSeconds</c>
    /// rather than passing bare "now", so a bid legitimately placed right at
    /// <see cref="Auction.AuctionEnd"/> whose own transaction is still committing gets a window
    /// to land before this auction is even considered a candidate. See
    /// <c>FinalizationOptions</c>'s remarks for the default's rationale.
    /// </remarks>
    Task<List<Auction>> GetExpiredUnfinalizedAsync(DateTime asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the local auction record <see cref="Auction.Finished"/> = <see langword="true"/>
    /// (Phase 11 Task 5.2 — consuming <c>Contracts.AuctionCancelled</c>). A no-op (never throws)
    /// when no local record exists for <paramref name="auctionId"/> — mirrors
    /// <see cref="InsertIfNotExistsAsync"/>'s tolerance of out-of-order/missing local state.
    /// </summary>
    /// <remarks>
    /// <b>Reuses <see cref="Auction.Finished"/> rather than a separate "Cancelled" flag:</b>
    /// this service only ever needs to know "is bidding on this auction over" — both a normal
    /// completion and an admin cancellation must (a) make <c>BidAppService.DetermineStatusAsync</c>
    /// return <see cref="Enums.BidStatus.Finished"/> for any further bid, and (b) make
    /// <see cref="GetExpiredUnfinalizedAsync"/>'s own <c>!Finished</c> filter permanently exclude
    /// this auction from the background finalizer's candidate set, so it can never publish
    /// <c>AuctionFinished</c> for it. <see cref="Auction.Finished"/> already satisfies both,
    /// unconditionally — no compare-and-swap is needed here (unlike
    /// <c>AuctionFinalizationUnitOfWork</c>'s conditional claim): this call never publishes an
    /// event itself (the Auction Service already published <c>AuctionCancelled</c>), so there is
    /// no "exactly once" race to guard against — setting the flag twice (redelivery) is a benign
    /// no-op either way. Idempotent by construction.
    /// </remarks>
    Task MarkFinishedAsync(Guid auctionId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the local auction record's <see cref="Auction.AuctionEnd"/> (Phase 11 Task 5.3 —
    /// consuming <c>Contracts.AuctionUpdated</c> when its <c>AuctionEnd</c> is non-null; this is
    /// how an admin's "end now" — <c>POST api/admin/auctions/{id}/end</c>, which sets
    /// <c>AuctionEnd = UtcNow</c> in the Auction Service — reaches this service's own background
    /// finalizer). A no-op (never throws) when no local record exists for
    /// <paramref name="auctionId"/>, mirroring <see cref="MarkFinishedAsync"/>'s tolerance.
    /// Idempotent: redelivery of the same event sets the same value again, a genuine no-op.
    /// </summary>
    Task UpdateAuctionEndAsync(Guid auctionId, DateTime auctionEnd, CancellationToken cancellationToken);
}
