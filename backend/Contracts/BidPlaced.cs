namespace Contracts;

public record BidPlaced(
    string Id,
    string AuctionId,
    string Bidder,
    DateTime BidTime,
    int Amount,
    string BidStatus
);
