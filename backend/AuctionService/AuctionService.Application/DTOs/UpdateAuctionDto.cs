namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>PUT api/auctions/{id}</c>. All fields are optional — only non-null
/// values are applied to the existing auction.
/// <para>
/// <b>Images:</b> when provided, replaces the existing gallery wholesale (all previous
/// <c>ItemImage</c> rows are deleted and replaced with the new list). When null, the
/// existing gallery is left unchanged. The same 1–10 image-count bound enforced on create
/// applies on update; the exact count validation is added in Phase 1 Task 18.6.
/// </para>
/// </summary>
public class UpdateAuctionDto
{
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Color { get; init; }
    public int? Mileage { get; init; }
    public int? Year { get; init; }

    /// <summary>
    /// When non-null, replaces the auction's image gallery wholesale.
    /// When null, the existing gallery is unchanged.
    /// </summary>
    public List<ImageDto>? Images { get; init; }
}
