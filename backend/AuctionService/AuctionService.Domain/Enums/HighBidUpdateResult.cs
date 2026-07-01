namespace AuctionService.Domain.Enums;

/// <summary>
/// Outcome of <see cref="Interfaces.IAuctionRepository.TryRaiseHighBidAsync"/>.
/// </summary>
public enum HighBidUpdateResult
{
    /// <summary>The auction's high bid was raised to the supplied amount.</summary>
    Raised,

    /// <summary>
    /// The auction exists but the bid did not qualify — either it did not beat the current
    /// high bid, or the auction is no longer <c>Live</c>. A benign, expected no-op.
    /// </summary>
    NotRaised,

    /// <summary>
    /// No auction with the supplied id exists in this service — a cross-service anomaly
    /// (e.g. a BidPlaced event referencing an auction the Auction Service never received).
    /// </summary>
    AuctionNotFound
}
