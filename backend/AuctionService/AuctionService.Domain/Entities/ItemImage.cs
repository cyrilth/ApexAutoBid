namespace AuctionService.Domain.Entities;

public class ItemImage
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public string? ThumbnailUrl { get; set; }

    // 0 = primary image (used by listings, search, and link previews)
    public int SortOrder { get; set; }

    // Navigation — owned by the Item (one-to-many); FK + back-reference
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;
}
