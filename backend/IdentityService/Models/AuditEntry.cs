namespace IdentityService.Models;

/// <summary>
/// Append-only audit record for an admin user-management action (Phase 11 Task 2.9 /
/// Requirements.md §13.3), persisted via <see cref="Data.ApplicationDbContext"/> in the same
/// database transaction as the admin mutation it describes (see
/// <see cref="Services.AdminUserService"/>). Mirrors
/// AuctionService.Domain.Entities.AuditEntry's shape exactly for cross-service consistency —
/// these are independently deployable services so no shared base type/project reference is
/// possible or appropriate.
/// <para>
/// Never exposed through any public API — inspected directly in the datastore only.
/// <c>Data</c> is a JSON payload summary and must never contain password material, tokens, or
/// secrets (a temporary password set via <c>POST api/admin/users/{id}/reset-password</c> is
/// returned once in that endpoint's response DTO, never written here).
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
