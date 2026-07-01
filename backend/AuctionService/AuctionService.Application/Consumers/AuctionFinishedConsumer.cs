using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AuctionService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionFinished"/> events published by the Bidding Service once an
/// auction's end time has passed, and finalizes the auction's <c>Winner</c>,
/// <c>WinnerEmail</c>, <c>SoldAmount</c>, and <c>Status</c> in the Auction Service's own
/// datastore.
/// </summary>
/// <remarks>
/// Idempotent by construction: once <c>Auction.Status</c> has moved off
/// <see cref="Status.Live"/>, the auction is treated as already finalized and the message is
/// acknowledged without rewriting any field. Redelivery of the same <see cref="AuctionFinished"/>
/// event is therefore a no-op.
/// </remarks>
public class AuctionFinishedConsumer(
    IAuctionRepository repository,
    ILogger<AuctionFinishedConsumer> logger) : IConsumer<AuctionFinished>
{
    public async Task Consume(ConsumeContext<AuctionFinished> context)
    {
        var message = context.Message;

        // A malformed AuctionId is a poison-message scenario, not a transient failure —
        // log and return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.AuctionId, out var auctionId))
        {
            logger.LogWarning(
                "AuctionFinished message had an unparsable AuctionId {AuctionId} — skipping",
                message.AuctionId);
            return;
        }

        var auction = await repository.GetByIdAsync(auctionId);
        if (auction is null)
        {
            logger.LogWarning("Auction {AuctionId} not found for AuctionFinished", auctionId);
            return;
        }

        // Idempotency guard: a Live auction has not yet been finalized. Anything else
        // (Finished, ReserveNotMet, Cancelled) means this event was already applied —
        // redelivery must not rewrite the already-settled outcome.
        if (auction.Status != Status.Live)
        {
            logger.LogInformation(
                "Auction {AuctionId} already finalized with status {Status} — AuctionFinished ignored",
                auctionId,
                auction.Status);
            return;
        }

        if (message.ItemSold)
        {
            auction.Winner = message.Winner;
            auction.WinnerEmail = message.WinnerEmail;
            auction.SoldAmount = message.Amount;
        }

        auction.Status = message.ItemSold ? Status.Finished : Status.ReserveNotMet;
        auction.UpdatedAt = DateTime.UtcNow;

        await repository.SaveChangesAsync();

        // Winner and WinnerEmail are intentionally never logged here.
        logger.LogInformation(
            "Auction {AuctionId} finalized with status {Status}",
            auctionId,
            auction.Status);
    }
}
