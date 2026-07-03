namespace SearchService.Domain.Entities;

/// <summary>
/// Flat, read-optimized search document synced from Auction Service events
/// (<c>AuctionCreated</c>, <c>AuctionUpdated</c>, <c>AuctionDeleted</c>, <c>AuctionFinished</c>,
/// <c>BidPlaced</c>, <c>BidRemoved</c>). Combines Auction + Item fields into a single denormalized
/// record optimized for search and filtering.
/// <para>
/// <b>Privacy:</b> <c>SellerEmail</c> and <c>WinnerEmail</c> are intentionally absent and never
/// stored in the search index — even though <c>AuctionFinished</c> carries <c>WinnerEmail</c>,
/// that field is ignored by this service. Contact details are exchanged exclusively via the
/// Auction Service API (see <c>Docs/Requirements.md</c> §3.1).
/// </para>
/// <para>
/// <b>Persistence-ignorant:</b> this is a pure POCO with zero external dependencies. MongoDB
/// mapping (collection name, indexes, the <c>MongoDB.Entities</c> base type) lives in the
/// Infrastructure layer, not here.
/// </para>
/// </summary>
public class Item
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Seller { get; set; }
    public string? Winner { get; set; }

    // --- Item fields (flattened) ---
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string Color { get; set; }
    public int Mileage { get; set; }

    /// <summary>
    /// The auction's primary image (<c>SortOrder = 0</c> in the Auction Service's gallery).
    /// The search index never stores the full gallery — that is served exclusively by
    /// <c>GET api/auctions/{id}</c>.
    /// </summary>
    public required string ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Auction status as a string (e.g., "Live", "Finished", "ReserveNotMet", "Cancelled").
    /// Kept as string, mirroring the event-contract convention, to decouple consumers from the
    /// Auction Service's Domain enum.
    /// </summary>
    public required string Status { get; set; }

    // --- Auction financials ---
    public int ReservePrice { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
}
