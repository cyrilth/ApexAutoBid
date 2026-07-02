namespace SearchService.Application.DTOs;

/// <summary>
/// Read DTO for a single search result. Mirrors Domain <c>Item</c> field-for-field (same
/// names/types/nullability) so the <c>Item</c> → <c>ItemDto</c> mapping in
/// <c>SearchAppService</c> is config-free, by-convention Mapster.
/// </summary>
/// <remarks>
/// Never carries an email field: the search index itself has none by design — see
/// <c>SearchService.Domain.Entities.Item</c>'s XML doc (Requirements §3.1). DTOs live at the
/// API boundary; the controller never returns <c>Item</c>/<c>ItemDocument</c> directly.
/// </remarks>
public class ItemDto
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

    public required string ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }

    public required string Status { get; init; }

    // --- Auction financials ---
    public int ReservePrice { get; init; }
    public int? SoldAmount { get; init; }
    public int? CurrentHighBid { get; init; }
}
