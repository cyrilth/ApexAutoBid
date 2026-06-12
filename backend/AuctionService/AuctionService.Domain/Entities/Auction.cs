using AuctionService.Domain.Enums;

namespace AuctionService.Domain.Entities;

public class Auction
{
    public Guid Id { get; set; }
    public int ReservePrice { get; set; } = 0;
    public required string Seller { get; set; }
    public required string SellerEmail { get; set; }
    public string? Winner { get; set; }
    public string? WinnerEmail { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime AuctionEnd { get; set; }
    public Status Status { get; set; } = Status.Live;

    // Navigation — one-to-one with Item
    public Item Item { get; set; } = null!;
}
