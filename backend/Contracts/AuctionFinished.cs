namespace Contracts;

public record AuctionFinished(
    bool ItemSold,
    string AuctionId,
    string? Winner,
    string? WinnerEmail,
    string Seller,
    int? Amount
);
