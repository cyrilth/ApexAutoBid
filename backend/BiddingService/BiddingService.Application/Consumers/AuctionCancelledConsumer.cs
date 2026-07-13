using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BiddingService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCancelled"/> (Phase 11 Task 5.2 / Requirements §10.2) — marks the
/// local <see cref="Domain.Entities.Auction"/> projection <c>Finished</c> so (a) any further
/// <c>PlaceBid</c> attempt is refused with the same <c>BidStatus.Finished</c> shape a normally-
/// ended auction already produces (<c>BidAppService.DetermineStatusAsync</c> — unchanged), and
/// (b) the background finalizer's <c>GetExpiredUnfinalizedAsync</c> permanently excludes it, so
/// it can never publish <c>AuctionFinished</c> for a cancelled auction.
/// </summary>
/// <remarks>
/// Idempotent by construction: <see cref="IAuctionRepository.MarkFinishedAsync"/> unconditionally
/// sets <c>Finished = true</c> — redelivery of the same <see cref="AuctionCancelled"/> event is a
/// genuine no-op (setting an already-true flag to true again), never an error, and never
/// republishes anything (this consumer publishes nothing at all — <c>AuctionCancelled</c> was
/// already published once, by the Auction Service).
/// </remarks>
public class AuctionCancelledConsumer(
    IAuctionRepository repository,
    ILogger<AuctionCancelledConsumer> logger) : IConsumer<AuctionCancelled>
{
    public async Task Consume(ConsumeContext<AuctionCancelled> context)
    {
        var message = context.Message;
        var auctionId = Guid.Parse(message.AuctionId);

        await repository.MarkFinishedAsync(auctionId, context.CancellationToken);

        logger.LogInformation(
            "Marked local auction {AuctionId} finished following AuctionCancelled (seller {Seller})",
            auctionId, message.Seller);
    }
}
