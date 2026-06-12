using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>POST api/auctions</c>.
/// <para>
/// <b>Images:</b> the ordered list of images for the new auction's gallery.
/// The first entry (lowest <c>SortOrder</c>) becomes the primary image shown in listings and
/// search results. At least one image is required. The configurable upper bound (default 10,
/// overridable via <c>Images__MaxPerAuction</c>) is enforced in Phase 1 Task 18.6 — do not
/// add a hardcoded count annotation here.
/// </para>
/// <para>
/// <b>AuctionEnd:</b> chosen by the seller; validated server-side against the platform's
/// configurable min/max duration bounds (<c>Auction__MinDuration</c> / <c>Auction__MaxDuration</c>,
/// or DB-stored admin values — see §3.1).
/// </para>
/// </summary>
public class CreateAuctionDto
{
    [Required]
    public required string Make { get; init; }

    [Required]
    public required string Model { get; init; }

    [Required]
    public required string Color { get; init; }

    [Required]
    public int Mileage { get; init; }

    [Required]
    public int Year { get; init; }

    /// <summary>
    /// Minimum bid required to sell. Defaults to 0 (no reserve).
    /// </summary>
    public int ReservePrice { get; init; } = 0;

    /// <summary>
    /// Ordered gallery images. At least one is required.
    /// The entry with the lowest <c>SortOrder</c> (typically 0) is the primary image.
    /// </summary>
    [Required]
    public required List<ImageDto> Images { get; init; }

    /// <summary>
    /// When the auction closes. Must satisfy the platform's min/max duration bounds.
    /// </summary>
    [Required]
    public DateTime AuctionEnd { get; init; }
}
