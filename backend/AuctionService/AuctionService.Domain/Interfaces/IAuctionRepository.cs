using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;

namespace AuctionService.Domain.Interfaces;

/// <summary>
/// Entity-level repository abstraction for <see cref="Auction"/> persistence.
/// Defined in Domain so that Application can depend on it without referencing
/// Infrastructure. Domain has zero external NuGet dependencies — only BCL and
/// Domain entity types appear in this interface.
/// </summary>
public interface IAuctionRepository
{
    /// <summary>
    /// Returns all auctions with <c>Item</c> and <c>Item.Images</c> eager-loaded,
    /// ordered by <c>Make</c> then <c>Model</c>. When <paramref name="updatedAfter"/>
    /// is supplied, only auctions with <c>UpdatedAt</c> strictly greater are returned.
    /// </summary>
    Task<List<Auction>> GetAllAsync(DateTime? updatedAfter);

    /// <summary>
    /// Returns a single auction with <c>Item</c> and <c>Item.Images</c> eager-loaded,
    /// or <see langword="null"/> if no auction with the given <paramref name="id"/> exists.
    /// </summary>
    Task<Auction?> GetByIdAsync(Guid id);

    /// <summary>Stages the auction for insertion on the next <see cref="SaveChangesAsync"/> call.</summary>
    void Add(Auction auction);

    /// <summary>Stages the auction for deletion on the next <see cref="SaveChangesAsync"/> call.</summary>
    void Remove(Auction auction);

    /// <summary>
    /// Gallery swap: removes the item's existing <see cref="ItemImage"/> rows and assigns
    /// <paramref name="newImages"/> as the replacement list. Each image's
    /// <see cref="ItemImage.ItemId"/> is set to <paramref name="item"/>'s id.
    /// </summary>
    void ReplaceGallery(Item item, List<ItemImage> newImages);

    /// <summary>
    /// Flushes all pending changes to the data store.
    /// Returns <see langword="true"/> if at least one row was written.
    /// </summary>
    Task<bool> SaveChangesAsync();

    /// <summary>
    /// Atomically raises the auction's <c>CurrentHighBid</c> to <paramref name="amount"/>, but only
    /// when the auction is still <c>Live</c> and <paramref name="amount"/> strictly exceeds the stored
    /// high bid. The predicate is evaluated by the database in a single UPDATE, so concurrent
    /// <c>BidPlaced</c> messages for the same auction cannot lose updates and redelivery of an
    /// older/equal bid is a no-op. Also stamps <c>UpdatedAt</c>.
    /// Returns <see cref="HighBidUpdateResult.Raised"/> when the high bid was updated,
    /// <see cref="HighBidUpdateResult.NotRaised"/> when the auction exists but the bid did not qualify,
    /// or <see cref="HighBidUpdateResult.AuctionNotFound"/> when no auction with that id exists.
    /// </summary>
    Task<HighBidUpdateResult> TryRaiseHighBidAsync(Guid auctionId, int amount);
}
