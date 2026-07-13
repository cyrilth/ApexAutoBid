namespace Contracts;

public record BannerPublished(
    Guid Id,
    string Message,
    string Scope,
    string? AuctionId,
    DateTime ActiveFrom,
    DateTime ActiveUntil
);
