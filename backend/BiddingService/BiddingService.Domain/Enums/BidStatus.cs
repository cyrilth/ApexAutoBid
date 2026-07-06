namespace BiddingService.Domain.Enums;

/// <summary>
/// Outcome of a placed bid (Requirements §3.3). Names match exactly — both the wire value on
/// <c>Contracts.BidPlaced.BidStatus</c> (via <c>.ToString()</c>) and the string comparisons
/// already shipped in <c>AuctionService</c>/<c>SearchService</c>'s <c>BidPlacedConsumer</c>
/// (e.g. <c>"Accepted"</c>, <c>"AcceptedBelowReserve"</c>) depend on these literal names.
/// </summary>
public enum BidStatus
{
    /// <summary>Higher than the current high bid and meets or exceeds the reserve price.</summary>
    Accepted,

    /// <summary>Higher than the current high bid but below the reserve price.</summary>
    AcceptedBelowReserve,

    /// <summary>Not higher than the current high bid.</summary>
    TooLow,

    /// <summary>Placed after the auction's <c>AuctionEnd</c> — recorded but never counted.</summary>
    Finished
}
