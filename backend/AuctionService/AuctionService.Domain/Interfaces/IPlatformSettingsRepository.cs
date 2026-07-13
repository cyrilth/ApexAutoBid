using AuctionService.Domain.Entities;

namespace AuctionService.Domain.Interfaces;

/// <summary>
/// Entity-level repository abstraction for the single <see cref="PlatformSettings"/> row
/// (Requirements §10.2 — Phase 11 Task 3.8).
/// </summary>
public interface IPlatformSettingsRepository
{
    /// <summary>
    /// Returns the current settings row, or <see langword="null"/> when no admin override has
    /// ever been saved (callers then fall back to configuration/environment defaults).
    /// </summary>
    Task<PlatformSettings?> GetAsync();

    /// <summary>Stages a brand-new settings row for insertion (used only the first time an admin saves).</summary>
    void Add(PlatformSettings settings);

    /// <summary>
    /// Stages an append-only <see cref="AuditEntry"/> for insertion on the next
    /// <see cref="SaveChangesAsync"/> call, so it commits atomically with the mutation it
    /// records (Requirements §13.3).
    /// </summary>
    void AddAudit(AuditEntry entry);

    /// <summary>Flushes all pending changes to the data store.</summary>
    Task<bool> SaveChangesAsync();
}
