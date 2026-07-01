namespace AuctionService.Application.DTOs;

/// <summary>
/// Read DTO returned by GET endpoints. Flattens Auction + Item fields into a single object.
/// <para>
/// <b>Privacy:</b> <c>SellerEmail</c> and <c>WinnerEmail</c> are intentionally absent. They are
/// post-sale contact-exchange fields exposed selectively (only to the counterparty) by a dedicated
/// response path added in a later phase — never returned here.
/// </para>
/// <para>
/// <b>Status</b> is a <c>string</c> rather than the Domain <c>Status</c> enum so that API consumers
/// remain decoupled from the server-side enum definition. This mirrors the event-contract convention
/// where events carry Status as a plain string, ensuring a new enum value (e.g., <c>Suspended</c>)
/// does not break existing clients.
/// </para>
/// </summary>
public class AuctionDto
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

    /// <summary>
    /// Auction status as a string (e.g., "Live", "Finished", "ReserveNotMet", "Cancelled").
    /// Kept as string to decouple consumers from the Domain enum.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The auction's image gallery, ordered by <c>SortOrder</c> ascending.
    /// Index 0 (<c>SortOrder = 0</c>) is the primary image.
    /// </summary>
    public List<ImageDto> Images { get; init; } = [];
}
