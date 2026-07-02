using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace SearchService.Infrastructure.Data;

/// <summary>
/// The persistence twin of <see cref="SearchService.Domain.Entities.Item"/>.
/// <para>
/// <c>MongoDB.Entities</c> requires collection types to implement its <see cref="IEntity"/>
/// interface, but the Domain <c>Item</c> is a deliberately dependency-free POCO (see its XML
/// doc). Rather than pull a MongoDB dependency into Domain, this Infrastructure-layer type
/// mirrors every field of <c>Item</c> exactly — same names, types, nullability, and
/// <c>required</c> modifiers — and carries the MongoDB-specific plumbing on top.
/// </para>
/// <para>
/// <b>Keep in lockstep:</b> any field added to, removed from, or retyped on the Domain
/// <c>Item</c> must be mirrored here (and vice versa is not required — this type may carry
/// extra persistence-only members, though it currently does not).
/// </para>
/// <para>
/// <b>Deliberately does not inherit</b> Domain <c>Item</c> (property-hiding / class-map
/// conflicts with MongoDB.Entities' driver-level BSON class map) and <b>does not inherit</b>
/// <c>MongoDB.Entities.Entity</c> (that base type forces a string <c>ObjectId</c> as the ID;
/// this document instead uses the auction's own <see cref="Guid"/> as <c>_id</c> so that
/// events consumed from Auction Service can upsert idempotently by that Guid).
/// </para>
/// </summary>
[Collection("Items")]
public sealed class ItemDocument : IEntity
{
    // BsonGuidRepresentation is required explicitly: without it, the driver's GuidSerializer
    // throws "cannot deserialize a Guid when GuidRepresentation is Unspecified" the moment a
    // real document exists (discovered during Task 5 code-review verification — reading back
    // any Guid _id failed). Standard is BSON binary subtype 4 (RFC 4122 UUID), the modern,
    // cross-driver-compatible representation — also what mongosh's UUID() helper produces by
    // default, so ids written by either the app or an operator via mongosh round-trip identically.
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// Formality required by <see cref="IEntity"/> for entities that don't yet have their ID
    /// set at save time. In practice, every <c>ItemDocument</c> upsert supplies the auction's
    /// Guid explicitly (from the triggering event), so this is never relied upon.
    /// </summary>
    public object GenerateNewID() => Guid.NewGuid();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Seller { get; set; }
    public string? Winner { get; set; }

    // --- Item fields (flattened) ---
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string Color { get; set; }
    public int Mileage { get; set; }

    public required string ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    public required string Status { get; set; }

    // --- Auction financials ---
    public int ReservePrice { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
}
