namespace BiddingService.Domain.Entities;

/// <summary>
/// Append-only audit record for a mutating admin operation, persisted in this service's own
/// MongoDB datastore (Requirements §13.3). Mirrors
/// <c>AuctionService.Domain.Entities.AuditEntry</c>'s exact shape (a distinct type — different
/// assembly, these two services never share code) — schema is dictated by Requirements §13.3,
/// not by convenience.
/// <para>
/// Bid placement itself needs no separate audit entry — the persisted <see cref="Bid"/> history
/// (bidder, time, amount, status) already fully audits it (Requirements §13.3's coverage table).
/// The only Bidding Service operation that writes an <see cref="AuditEntry"/> is admin bid
/// removal (<c>AdminBidAppService.RemoveBidAsync</c>) — never exposed via any public API.
/// </para>
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Actor { get; set; }
    public bool ActorIsAdmin { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Data { get; set; }
}
