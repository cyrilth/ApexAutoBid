namespace AuctionService.Domain.Entities;

/// <summary>
/// Append-only audit record for a mutating operation, persisted in the owning service's own
/// datastore in the SAME transaction as the mutation (Requirements §13.3). Never exposed via API.
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
