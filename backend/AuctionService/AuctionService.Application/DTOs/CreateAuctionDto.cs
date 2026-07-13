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

    // No [Required] on value types — a non-nullable int/DateTime can never be
    // null, so the attribute is a no-op (and misleadingly implies 0 is rejected).
    public int Mileage { get; init; }

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
    /// When the auction closes. For non-admin callers, must satisfy the platform's min/max
    /// duration bounds (Phase 11 Task 3.4; resolution order: DB <c>PlatformSettings</c> →
    /// <c>Auction:MinDuration</c>/<c>Auction:MaxDuration</c> config → defaults 1 hour–90 days).
    /// Admin callers are exempt from these bounds.
    /// </summary>
    public DateTime AuctionEnd { get; init; }

    /// <summary>
    /// Optional explicit seller (Requirements §3.1/§10.2 — Phase 11 Task 3.1). Honored ONLY
    /// when the caller is in the "admin" role — admins may create an auction on behalf of any
    /// user, including themselves, by supplying their own username here. For every other
    /// caller this field is silently IGNORED regardless of its value: the seller always stays
    /// the caller's own <c>username</c> claim.
    /// </summary>
    public string? Seller { get; init; }

    /// <summary>
    /// Optional explicit seller email, paired with <see cref="Seller"/> for admin-created
    /// auctions. Also ignored for non-admin callers. When an admin supplies <see cref="Seller"/>
    /// without this field, <c>SellerEmail</c> is resolved to an empty string rather than
    /// guessed — it is never inferred from <see cref="Seller"/>.
    /// </summary>
    public string? SellerEmail { get; init; }
}
