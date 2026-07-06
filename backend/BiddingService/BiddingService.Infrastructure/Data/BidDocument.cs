using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// The persistence twin of <see cref="BiddingService.Domain.Entities.Bid"/>. Mirrors every
/// field except <see cref="BidStatus"/>, which is stored as its enum member's name (e.g.
/// <c>"Accepted"</c>) rather than the Domain <c>BidStatus</c> enum — human-readable in the
/// database, safe against enum reordering, and matches the <c>Contracts.BidPlaced.BidStatus</c>
/// wire shape exactly.
/// <para>
/// Because <c>BidStatus</c> is an enum on the Domain side but a string here, this pair is
/// <b>not</b> a pure by-convention Mapster mapping (contrast <see cref="AuctionDocument"/>,
/// which mirrors <c>Auction</c> with no such mismatch) — see <see cref="BidDocumentMapper"/>
/// for the explicit, hand-written conversion this uses instead.
/// </para>
/// <para>
/// Deliberately does not inherit <c>MongoDB.Entities.Entity</c> (that base type forces a
/// string <c>ObjectId</c> as the ID); this document instead uses the bid's own
/// <see cref="Guid"/> as <c>_id</c>, mirroring <c>SearchService</c>'s <c>ItemDocument</c>.
/// </para>
/// </summary>
[Collection("Bids")]
public sealed class BidDocument : IEntity
{
    // BsonGuidRepresentation is required explicitly on every Guid property (not just the
    // [BsonId] one) — live-verified during this task: the driver's GuidSerializer throws
    // "cannot serialize/deserialize a Guid when GuidRepresentation is Unspecified" without it.
    // Standard is BSON binary subtype 4 (RFC 4122 UUID), the modern, cross-driver-compatible
    // representation — same choice SearchService's ItemDocument makes for its own Guid _id.
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// Formality required by <see cref="IEntity"/> for entities that don't yet have their ID
    /// set at save time. In practice every <c>BidDocument</c> insert supplies the bid's Guid
    /// explicitly (<c>Guid.NewGuid()</c>, assigned in <c>BidAppService.PlaceBidAsync</c>), so
    /// this is never relied upon.
    /// </summary>
    public object GenerateNewID() => Guid.NewGuid();

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid AuctionId { get; set; }

    public required string Bidder { get; set; }

    /// <summary>Never returned by any bids API response — see <c>Bid.BidderEmail</c>'s XML doc.</summary>
    public required string BidderEmail { get; set; }

    public DateTime BidTime { get; set; }
    public int Amount { get; set; }
    public required string BidStatus { get; set; }
}
