using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// The persistence twin of this service's local <see cref="BiddingService.Domain.Entities.Auction"/>
/// projection. Every field <c>Auction</c> itself has matches, by name and type — a pure
/// by-convention Mapster mapping for those (see <see cref="AuctionRepository"/>), unlike
/// <see cref="BidDocument"/>'s enum/string mismatch against <c>Bid</c>. <see cref="CurrentHigh"/>
/// is the one exception: a Mongo-only field with no <c>Auction</c> counterpart at all (see its
/// own remarks) — Mapster's ambient <c>.Adapt&lt;T&gt;()</c> still safely leaves it at its
/// default (<c>0</c>) on every mapping from <c>Auction</c>, which is exactly the correct
/// initial value for a brand-new local auction record.
/// </summary>
[Collection("Auctions")]
public sealed class AuctionDocument : IEntity
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// Formality required by <see cref="IEntity"/> — never relied upon, since every upsert
    /// supplies the auction's own Guid explicitly (from the triggering <c>AuctionCreated</c>
    /// event).
    /// </summary>
    public object GenerateNewID() => Guid.NewGuid();

    public DateTime AuctionEnd { get; set; }
    public required string Seller { get; set; }
    public int ReservePrice { get; set; }
    public bool Finished { get; set; }

    /// <summary>
    /// The highest <c>Amount</c> among this auction's <c>Accepted</c>/<c>AcceptedBelowReserve</c>
    /// bids, maintained atomically by <c>BidPlacementUnitOfWork</c> (phase-end code review
    /// Critical 1) as part of the SAME Mongo transaction that inserts the accepting bid and
    /// publishes <c>BidPlaced</c> — a conditional <c>FindOneAndUpdate</c> ("claim") only
    /// succeeds while <c>CurrentHigh &lt; </c>the new bid's <c>Amount</c>, making "two
    /// concurrent bids both read the same stale high and both get accepted" structurally
    /// impossible: whichever transaction's claim commits first wins, and the other necessarily
    /// re-evaluates against the now-updated value. Defaults to <c>0</c> for a brand-new
    /// auction (no bids yet), so any positive-amount first bid always beats it.
    /// <c>BiddingService.Domain.Entities.Auction</c> deliberately
    /// has no counterpart property (see its own remarks) — nothing outside this Infrastructure
    /// project ever needs to read it directly.
    /// </summary>
    public int CurrentHigh { get; set; }
}
