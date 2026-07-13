using AuctionService.Application.DTOs;

namespace AuctionService.Application.Services;

/// <summary>Result codes for banner write operations (Phase 11 Task 3.5).</summary>
public enum BannerWriteResult
{
    Success,
    NotFound,

    /// <summary><c>Scope</c> was not exactly "Global", "HomePage", or "Auction".</summary>
    InvalidScope,

    /// <summary><c>Scope</c> was "Auction" but <c>AuctionId</c> was omitted.</summary>
    MissingAuctionId,

    /// <summary><c>Scope</c> was not "Auction" but <c>AuctionId</c> was supplied anyway.</summary>
    UnexpectedAuctionId,

    /// <summary><c>ActiveFrom</c> was not strictly earlier than <c>ActiveUntil</c>.</summary>
    InvalidDateRange
}

/// <summary>
/// Result of <see cref="IBannerService.CreateAsync"/>. <see cref="Banner"/> is non-null only
/// when <see cref="Status"/> is <see cref="BannerWriteResult.Success"/>.
/// </summary>
public record BannerCreateResult(BannerWriteResult Status, BannerDto? Banner);

/// <summary>
/// Application-level service for banner messages (Requirements §10.3 — Phase 11 Task 3.5).
/// Every mutating method here is reached only through admin-only controllers — there is no
/// per-call <c>isAdmin</c> parameter because, unlike <c>IAuctionService</c> (shared between
/// sellers and admins), a banner mutation is ALWAYS performed by an admin.
/// </summary>
public interface IBannerService
{
    /// <summary>Admin listing — every banner regardless of its active window, most recent first.</summary>
    Task<List<BannerDto>> GetAllAsync();

    /// <summary>
    /// Public read — currently-active banners (<c>ActiveFrom &lt;= now &lt;= ActiveUntil</c>),
    /// optionally filtered by <paramref name="scope"/> ("Global" | "HomePage" | "Auction") and/or
    /// <paramref name="auctionId"/>. An unrecognized <paramref name="scope"/> value yields an
    /// empty list rather than an error — this is a filter on a public anonymous read, not a
    /// mutating command.
    /// </summary>
    Task<List<BannerDto>> GetActiveAsync(string? scope, Guid? auctionId);

    /// <summary>
    /// Creates a new banner and emits <c>BannerPublished</c> via the outbox. Writes an
    /// append-only <c>AuditEntry</c> ("BannerCreated") in the same <c>SaveChanges</c>
    /// (Requirements §13.3).
    /// </summary>
    Task<BannerCreateResult> CreateAsync(CreateBannerDto dto, string createdBy);

    /// <summary>
    /// Fully replaces an existing banner and emits <c>BannerPublished</c> via the outbox.
    /// Writes an append-only <c>AuditEntry</c> ("BannerUpdated") in the same <c>SaveChanges</c>.
    /// </summary>
    Task<BannerWriteResult> UpdateAsync(Guid id, UpdateBannerDto dto, string updatedBy);

    /// <summary>
    /// Deletes a banner. Writes an append-only <c>AuditEntry</c> ("BannerDeleted") in the same
    /// <c>SaveChanges</c>. No event is emitted on delete (only create/update publish
    /// <c>BannerPublished</c>).
    /// </summary>
    Task<BannerWriteResult> DeleteAsync(Guid id, string deletedBy);
}
