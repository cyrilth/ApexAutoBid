namespace BiddingService.Domain.Entities;

/// <summary>
/// Minimal local projection of an Auction Service auction (Architecture.md §4.2), synced from
/// the <c>AuctionCreated</c> event. Carries only what bid validation needs — <see cref="AuctionEnd"/>,
/// <see cref="Seller"/>, <see cref="ReservePrice"/>, <see cref="Finished"/> — per
/// Requirements §3.3's <c>Auction.cs (local)</c> model.
/// <para>
/// <b>Deliberately does not carry a CurrentHighBid field.</b> The pre-check a new bid is
/// evaluated against still derives its (necessarily non-authoritative) guess from this
/// service's own persisted bid history (<c>IBidRepository.GetHighestAcceptedAmountAsync</c>).
/// The actual, race-proof current-high value lives ONLY on the Infrastructure-layer
/// <c>AuctionDocument.CurrentHigh</c> (phase-end code review Critical 1) — a Mongo-only field
/// this Domain projection deliberately does not mirror, precisely because Domain has zero
/// external dependencies and no business logic here needs to read it directly: the atomic
/// claim-and-compare happens entirely inside <c>BidPlacementUnitOfWork</c>, one layer down.
/// </para>
/// <para>
/// This is a distinct type from <c>AuctionService.Domain.Entities.Auction</c> (different
/// assembly, different — much larger — set of fields) even though the name and namespace
/// segment are the same; the two services never share code.
/// </para>
/// </summary>
public class Auction
{
    public Guid Id { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Seller { get; set; }
    public int ReservePrice { get; set; }

    /// <summary>
    /// Set by the (later) <c>AuctionCancelled</c> consumer / background finalizer — Phase 11
    /// and the later background-finalizer run, respectively. Always <see langword="false"/>
    /// for a freshly-consumed <c>AuctionCreated</c>. Bid placement treats an auction as ended
    /// when either this is <see langword="true"/> or <see cref="AuctionEnd"/> has passed.
    /// </summary>
    public bool Finished { get; set; }
}
