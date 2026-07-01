namespace AuctionService.Application.DTOs;

/// <summary>
/// Response DTO for <c>POST api/auctions/thumbnail</c>. The client stores the returned URL
/// on the matching <see cref="ImageDto.ThumbnailUrl"/> before submitting the create/update
/// auction form.
/// </summary>
public class ThumbnailResponse
{
    /// <summary>The public URL of the generated WebP thumbnail (max 400px wide).</summary>
    public required string ThumbnailUrl { get; init; }
}
