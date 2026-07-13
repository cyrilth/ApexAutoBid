namespace Contracts;

public record BidRemoved(
    string BidId,
    string AuctionId,
    int? CurrentHighBid
);
