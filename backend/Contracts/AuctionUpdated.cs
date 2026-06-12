namespace Contracts;

public record AuctionUpdated(
    string Id,
    string Make,
    string Model,
    string Color,
    int Mileage,
    int Year,
    DateTime? AuctionEnd
);
