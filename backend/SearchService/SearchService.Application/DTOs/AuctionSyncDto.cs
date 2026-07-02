namespace SearchService.Application.DTOs;

/// <summary>
/// Mirrors the wire shape of AuctionService's <c>AuctionDto</c> (<c>GET
/// api/auctions[?date=]</c>), used by <c>IAuctionServiceClient</c>/<c>DataSyncService</c>
/// (Phase 2 Task 6 HTTP polling fallback). Owned by Application, not Infrastructure — the
/// Clean Architecture DTO placement rule — even though only Infrastructure's
/// <c>AuctionServiceHttpClient</c> ever deserializes it.
/// </summary>
/// <remarks>
/// <b>Privacy:</b> deliberately has NO SellerEmail/WinnerEmail properties. AuctionDto itself
/// never carries them (see its own XML doc), but even if it did, omitting them here means
/// <c>System.Text.Json</c> silently drops any such field on deserialization — emails must
/// never enter this service under any code path (Requirements §3.1).
/// </remarks>
public class AuctionSyncDto
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime AuctionEnd { get; init; }
    public required string Seller { get; init; }
    public string? Winner { get; init; }

    // --- Item fields (flattened) ---
    public required string Make { get; init; }
    public required string Model { get; init; }
    public int Year { get; init; }
    public required string Color { get; init; }
    public int Mileage { get; init; }

    // --- Auction financials ---
    public int ReservePrice { get; init; }
    public int? SoldAmount { get; init; }
    public int? CurrentHighBid { get; init; }

    public required string Status { get; init; }

    /// <summary>
    /// The full gallery, ordered by <c>SortOrder</c> ascending (AuctionService serializes it
    /// pre-ordered — see <c>AuctionMappingConfig</c>'s <c>Auction → AuctionDto</c> rule).
    /// <c>ItemMappingConfig</c>'s <c>AuctionSyncDto → Item</c> mapping still re-derives the
    /// primary image via its own <c>OrderBy(SortOrder)</c> rather than blindly trusting
    /// <c>Images[0]</c>, since this DTO crosses a process/service boundary.
    /// </summary>
    public List<AuctionSyncImageDto> Images { get; init; } = [];
}

/// <summary>A single gallery image, mirroring AuctionService's <c>ImageDto</c> wire shape.</summary>
public class AuctionSyncImageDto
{
    public required string Url { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int SortOrder { get; init; }
}
