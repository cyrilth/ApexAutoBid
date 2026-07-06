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
}
