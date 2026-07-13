using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AuctionService.Application.Consumers;

/// <summary>
/// Consumes <see cref="BidRemoved"/> events published by the Bidding Service after an admin
/// deletes a bid (Requirements §10.2 — Phase 11 Task 3.6): refreshes
/// <c>Auction.CurrentHighBid</c> to the event's already-recalculated authoritative value.
/// </summary>
/// <remarks>
/// Idempotent by construction: this is an unconditional overwrite of <c>CurrentHighBid</c> to
/// the value the Bidding Service already recalculated (not a "raise only if higher" comparison
/// like <see cref="BidPlacedConsumer"/>) — redelivering the same event re-applies the same
/// value, a no-op in effect.
/// </remarks>
public class BidRemovedConsumer(
    IAuctionRepository repository,
    ILogger<BidRemovedConsumer> logger) : IConsumer<BidRemoved>
{
    public async Task Consume(ConsumeContext<BidRemoved> context)
    {
        var message = context.Message;

        // A malformed AuctionId is a poison-message scenario, not a transient failure —
        // log and return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.AuctionId, out var auctionId))
        {
            logger.LogWarning(
                "BidRemoved message had an unparsable AuctionId {AuctionId} — skipping",
                message.AuctionId);
            return;
        }

        var auction = await repository.GetByIdAsync(auctionId);
        if (auction is null)
        {
            logger.LogWarning("Auction {AuctionId} not found for BidRemoved", auctionId);
            return;
        }

        auction.CurrentHighBid = message.CurrentHighBid;
        auction.UpdatedAt = DateTime.UtcNow;

        await repository.SaveChangesAsync();

        logger.LogInformation(
            "Auction {AuctionId} CurrentHighBid refreshed to {CurrentHighBid} after BidRemoved",
            auctionId, message.CurrentHighBid);
    }
}
