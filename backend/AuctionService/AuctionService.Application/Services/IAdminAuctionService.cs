using AuctionService.Application.DTOs;

namespace AuctionService.Application.Services;

/// <summary>Result codes for admin auction-moderation actions (Phase 11 Task 3.2/3.3).</summary>
public enum AdminAuctionWriteResult
{
    Success,
    NotFound
}

/// <summary>
/// Application-level service for admin auction moderation (Requirements §3.1/§10.2 — Phase 11
/// Task 3.2/3.3/3.7). Kept separate from <see cref="IAuctionService"/> — that interface is
/// shared between ordinary sellers and admins for seller-owned CRUD, whereas every method here
/// is reached only through the admin-only <c>AdminAuctionsController</c> (the "AdminOnly" policy
/// already guarantees the caller is an admin, so there is no per-call <c>isAdmin</c> parameter).
/// </summary>
public interface IAdminAuctionService
{
    /// <summary>
    /// Sets <c>AuctionEnd = UtcNow</c> immediately and emits <c>AuctionUpdated</c> (including
    /// the new <c>AuctionEnd</c>) — the Bidding Service's background finalization job then
    /// finalizes the now-expired auction through its normal polling flow (winner,
    /// <c>AuctionFinished</c>, notifications); no direct call is made here. Writes an
    /// append-only <c>AuditEntry</c> ("AuctionEndedByAdmin") in the same <c>SaveChanges</c>.
    /// </summary>
    Task<AdminAuctionWriteResult> EndAuctionAsync(Guid id, string admin);

    /// <summary>
    /// Sets <c>Status = Cancelled</c> and emits <c>AuctionCancelled</c>. Writes an append-only
    /// <c>AuditEntry</c> ("AuctionCancelledByAdmin") in the same <c>SaveChanges</c>.
    /// </summary>
    Task<AdminAuctionWriteResult> CancelAuctionAsync(Guid id, string admin);

    /// <summary>Returns auction counts by status (and the overall total) for the admin dashboard.</summary>
    Task<AuctionStatsDto> GetStatsAsync();
}
