namespace AuctionService.Domain.Enums;

/// <summary>
/// Where a <see cref="Entities.Banner"/> is shown (Requirements §10.3). Mirrors the string
/// values carried by the <c>BannerPublished</c> event contract (<c>Contracts.BannerPublished</c>)
/// exactly — "Global" | "HomePage" | "Auction" — so the enum name round-trips losslessly through
/// the event payload without a lookup table.
/// </summary>
public enum BannerScope
{
    /// <summary>Shown everywhere across the site.</summary>
    Global,

    /// <summary>Shown only on the home page.</summary>
    HomePage,

    /// <summary>Shown only on a specific auction's page (requires <see cref="Entities.Banner.AuctionId"/>).</summary>
    Auction
}
