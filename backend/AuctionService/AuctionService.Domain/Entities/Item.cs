namespace AuctionService.Domain.Entities;

public class Item
{
    public Guid Id { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string Color { get; set; }
    public int Mileage { get; set; }

    // Ordered gallery (1–10 per item); SortOrder 0 is the primary image
    public List<ItemImage> Images { get; set; } = [];

    // Navigation — owned by the Auction (one-to-one); FK + back-reference
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
}
