using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// The persistence twin of <see cref="BiddingService.Domain.Entities.AuditEntry"/> — a pure
/// by-convention field-for-field mirror (every property matches by name and type), inserted by
/// <see cref="BidRemovalUnitOfWork"/> in the SAME Mongo operation scope as the bid deletion, the
/// <c>AuctionDocument.CurrentHigh</c> recalculation, and the <c>BidRemoved</c> publish
/// (Requirements §13.3). Never read back by this service — append-only, and not exposed through
/// any public API (Requirements §13.3).
/// </summary>
[Collection("AuditEntries")]
public sealed class AuditEntryDocument : IEntity
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>
    /// Formality required by <see cref="IEntity"/> — never relied upon, since every insert
    /// supplies the entry's own Guid explicitly (<c>AdminBidAppService.RemoveBidAsync</c>).
    /// </summary>
    public object GenerateNewID() => Guid.NewGuid();

    public DateTime Timestamp { get; set; }
    public required string Actor { get; set; }
    public bool ActorIsAdmin { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Data { get; set; }
}
