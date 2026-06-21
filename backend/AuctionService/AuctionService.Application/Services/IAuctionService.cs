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
    SaveFailed
}

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
    /// Creates a new auction. Returns the created DTO, or <see langword="null"/> if
    /// the save operation wrote zero rows.
    /// </summary>
    Task<AuctionDto?> CreateAuctionAsync(CreateAuctionDto dto, string seller, string sellerEmail);

    /// <summary>Partially updates an existing auction. Returns a <see cref="AuctionWriteResult"/>.</summary>
    Task<AuctionWriteResult> UpdateAuctionAsync(Guid id, UpdateAuctionDto dto, string requestingUser);

    /// <summary>Deletes an existing auction. Returns a <see cref="AuctionWriteResult"/>.</summary>
    Task<AuctionWriteResult> DeleteAuctionAsync(Guid id, string requestingUser);
}
