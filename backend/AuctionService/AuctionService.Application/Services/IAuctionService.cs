using AuctionService.Application.DTOs;

namespace AuctionService.Application.Services;

/// <summary>
/// Result codes for write operations so the controller can map outcomes to HTTP
/// status codes without the Application layer having any knowledge of HTTP.
/// </summary>
public enum AuctionWriteResult
{
    Success,
    NotFound,
    Forbidden,
    SaveFailed,

    /// <summary>
    /// The submitted image gallery failed server-side validation (Phase 1 Task 18.6):
    /// the image count was outside the configured 1..<c>Images:MaxPerAuction</c> bound, a
    /// platform-hosted image's actual size (verified via HEAD) exceeded <c>Images:MaxSizeMB</c>,
    /// or the gallery's <c>SortOrder</c> values were invalid (negative, duplicated, or missing
    /// the primary <c>0</c> — which would otherwise violate the unique <c>(ItemId, SortOrder)</c>
    /// index). The referenced objects are left untouched: the create/update is simply rejected,
    /// never deleted, because the server can't prove the caller owns a referenced object key.
    /// </summary>
    InvalidImages,

    /// <summary>
    /// The submitted <c>AuctionEnd</c> falls outside the platform's configured min/max auction
    /// duration bounds (Phase 11 Task 3.4). Only enforced for non-admin callers — an admin
    /// caller's <c>AuctionEnd</c> is never rejected on this basis, on either create or update.
    /// </summary>
    InvalidDuration
}

/// <summary>
/// Result of <see cref="IAuctionService.CreateAuctionAsync"/>. <see cref="Auction"/> is
/// non-null only when <see cref="Status"/> is <see cref="AuctionWriteResult.Success"/>.
/// </summary>
public record AuctionCreateResult(AuctionWriteResult Status, AuctionDto? Auction);

/// <summary>
/// Application-level service for auction operations.
/// Controllers depend only on this interface — never on <c>IAuctionRepository</c>
/// or any Infrastructure type.
/// </summary>
public interface IAuctionService
{
    /// <summary>
    /// Returns all auctions as DTOs. When <paramref name="updatedAfter"/> is supplied,
    /// only auctions with <c>UpdatedAt</c> strictly greater are returned.
    /// </summary>
    Task<List<AuctionDto>> GetAuctionsAsync(DateTime? updatedAfter);

    /// <summary>
    /// Returns a single auction as an <see cref="AuctionDetailDto"/>, or <see langword="null"/>
    /// if not found. <paramref name="requestingUser"/> is the caller's <c>username</c> claim
    /// (<see langword="null"/> for an unauthenticated/anonymous caller, or for the gRPC fallback
    /// rpc — see <c>AuctionGrpcService</c>) and drives ONLY the post-sale contact-exchange
    /// fields (Requirements §3.1 / Tasks.md Phase 5 Task 19): once the auction is sold
    /// (<c>Status = Finished</c> with a recorded <c>Winner</c>), the seller sees
    /// <see cref="AuctionDetailDto.WinnerEmail"/>, the winner sees
    /// <see cref="AuctionDetailDto.SellerEmail"/>, and every other caller — including
    /// <see langword="null"/> — sees neither.
    /// </summary>
    Task<AuctionDetailDto?> GetAuctionByIdAsync(Guid id, string? requestingUser);

    /// <summary>
    /// Creates a new auction. Validates the submitted gallery (Task 18.6) before writing
    /// anything; returns an <see cref="AuctionCreateResult"/> describing the outcome.
    /// An append-only <c>AuditEntry</c> ("AuctionCreated") is written in the same
    /// <c>SaveChanges</c> as the mutation (Requirements §13.3); <paramref name="isAdmin"/>
    /// is stamped onto that record and does not otherwise affect creation.
    /// </summary>
    Task<AuctionCreateResult> CreateAuctionAsync(
        CreateAuctionDto dto, string seller, string sellerEmail, bool isAdmin);

    /// <summary>
    /// Partially updates an existing auction. Returns a <see cref="AuctionWriteResult"/>.
    /// An append-only <c>AuditEntry</c> ("AuctionUpdated") is written in the same
    /// <c>SaveChanges</c> as the mutation (Requirements §13.3); <paramref name="isAdmin"/>
    /// is stamped onto that record and does not otherwise affect the update.
    /// </summary>
    Task<AuctionWriteResult> UpdateAuctionAsync(
        Guid id, UpdateAuctionDto dto, string requestingUser, bool isAdmin);

    /// <summary>
    /// Deletes an existing auction. Returns a <see cref="AuctionWriteResult"/>.
    /// An append-only <c>AuditEntry</c> ("AuctionDeleted") is written in the same
    /// <c>SaveChanges</c> as the mutation (Requirements §13.3); <paramref name="isAdmin"/>
    /// is stamped onto that record and does not otherwise affect deletion.
    /// </summary>
    Task<AuctionWriteResult> DeleteAuctionAsync(Guid id, string requestingUser, bool isAdmin);

    /// <summary>
    /// Returns the platform's currently-effective auction duration bounds (Phase 11 Task 3.8)
    /// — the same bounds <see cref="CreateAuctionAsync"/>/<see cref="UpdateAuctionAsync"/>
    /// validate a non-admin caller's <c>AuctionEnd</c> against. Backs the anonymous
    /// <c>GET api/auctions/duration-limits</c> endpoint.
    /// </summary>
    Task<DurationLimitsDto> GetDurationLimitsAsync();
}
