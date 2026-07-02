namespace SearchService.Domain.Enums;

/// <summary>
/// Outcome of <see cref="Interfaces.IItemRepository.TryRaiseHighBidAsync"/>.
/// </summary>
public enum HighBidUpdateResult
{
    /// <summary>The item's <c>CurrentHighBid</c> was raised to the supplied amount.</summary>
    Raised,

    /// <summary>
    /// The item exists but the bid did not qualify — either it did not beat the current
    /// high bid, or the item is no longer <c>Live</c>. A benign, expected no-op (also the
    /// correct outcome for redelivery of an older/equal bid).
    /// </summary>
    NotRaised,

    /// <summary>
    /// No item with the supplied id exists in this service's index — expected when a
    /// <c>BidPlaced</c> event for a given auction arrives before the corresponding
    /// <c>AuctionCreated</c> event has been processed (out-of-order delivery).
    /// </summary>
    ItemNotFound
}
