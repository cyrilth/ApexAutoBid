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
    InvalidImages
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
    /// Returns a single auction DTO, or <see langword="null"/> if not found.
    /// </summary>
    Task<AuctionDto?> GetAuctionByIdAsync(Guid id);

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
}
