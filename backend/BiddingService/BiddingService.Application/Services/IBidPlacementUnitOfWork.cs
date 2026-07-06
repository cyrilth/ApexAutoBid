using BiddingService.Domain.Entities;
using Contracts;

namespace BiddingService.Application.Services;

/// <summary>
/// Persists a newly placed <see cref="Bid"/> and — when <paramref name="bidPlacedEvent"/> is
/// supplied — publishes the corresponding <see cref="BidPlaced"/> event as a single atomic
/// operation, so a genuinely accepted bid can never be recorded without its event reaching
/// the bus, and a failed publish can never leave a "phantom" bid behind.
/// </summary>
/// <remarks>
/// Defined in Application (not Domain) because its contract legitimately mentions
/// <see cref="Contracts.BidPlaced"/> — Application already references the <c>Contracts</c>
/// project (see the NuGet placement table). The Infrastructure implementation
/// (<c>BidPlacementUnitOfWork</c>) additionally depends on <c>MassTransit.MongoDb</c>'s
/// transactional "bus outbox" (<c>MassTransit.MongoDbIntegration.MongoDbContext</c>) — an
/// Infrastructure-only package — to make the write and the publish atomic; see that class's
/// remarks for the exact, live-verified mechanics.
/// </remarks>
public interface IBidPlacementUnitOfWork
{
    /// <summary>
    /// Persists <paramref name="bid"/>. When <paramref name="bidPlacedEvent"/> is
    /// non-<see langword="null"/> (i.e. the bid's tentative status is
    /// <see cref="Domain.Enums.BidStatus.Accepted"/> or
    /// <see cref="Domain.Enums.BidStatus.AcceptedBelowReserve"/>), this call ALSO atomically
    /// re-verifies that tentative status against the auction's true current high — inside the
    /// same transaction as the write — before deciding whether to publish it (phase-end code
    /// review Critical 1).
    /// </summary>
    /// <remarks>
    /// <b>May mutate <paramref name="bid"/> in place:</b> if the atomic re-verification loses
    /// the race (some other bid already claimed at least this amount), the implementation
    /// downgrades <c>bid.BidStatus</c> to <see cref="Domain.Enums.BidStatus.TooLow"/> before
    /// persisting it and does NOT publish <paramref name="bidPlacedEvent"/> even though it was
    /// supplied non-<see langword="null"/> — callers must read <c>bid.BidStatus</c> back AFTER
    /// this call returns (not rely on whatever tentative value they set beforehand) for the
    /// authoritative outcome. See <c>BidPlacementUnitOfWork</c>'s remarks for the exact,
    /// live-verified mechanics and why a failed claim is not merely an approximation of TooLow.
    /// </remarks>
    Task SaveAsync(Bid bid, BidPlaced? bidPlacedEvent, CancellationToken cancellationToken);
}
