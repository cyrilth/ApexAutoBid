using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;

namespace AuctionService.Domain.Interfaces;

/// <summary>
/// Entity-level repository abstraction for <see cref="Banner"/> persistence (Requirements
/// §10.3 — Phase 11 Task 3.5). Mirrors <see cref="IAuctionRepository"/>'s shape: it owns its
/// own <see cref="AddAudit"/>/<see cref="SaveChangesAsync"/> pair so a banner mutation and its
/// append-only <see cref="AuditEntry"/> commit in the same transaction (Requirements §13.3),
/// even though both are backed by the same underlying DbContext as <c>IAuctionRepository</c>.
/// </summary>
public interface IBannerRepository
{
    /// <summary>Returns every banner (admin list), most recently active first.</summary>
    Task<List<Banner>> GetAllAsync();

    /// <summary>
    /// Returns banners currently active (<c>ActiveFrom &lt;= now &lt;= ActiveUntil</c>),
    /// optionally filtered by <paramref name="scope"/> and/or <paramref name="auctionId"/>.
    /// </summary>
    Task<List<Banner>> GetActiveAsync(BannerScope? scope, Guid? auctionId, DateTime now);

    /// <summary>Returns a single banner, or <see langword="null"/> if no banner with the given id exists.</summary>
    Task<Banner?> GetByIdAsync(Guid id);

    /// <summary>Stages the banner for insertion on the next <see cref="SaveChangesAsync"/> call.</summary>
    void Add(Banner banner);

    /// <summary>Stages the banner for deletion on the next <see cref="SaveChangesAsync"/> call.</summary>
    void Remove(Banner banner);

    /// <summary>
    /// Stages an append-only <see cref="AuditEntry"/> for insertion on the next
    /// <see cref="SaveChangesAsync"/> call, so it commits atomically with the mutation it
    /// records (Requirements §13.3).
    /// </summary>
    void AddAudit(AuditEntry entry);

    /// <summary>
    /// Flushes all pending changes to the data store.
    /// Returns <see langword="true"/> if at least one row was written.
    /// </summary>
    Task<bool> SaveChangesAsync();
}
