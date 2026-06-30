using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AuctionService.Application.Consumers;

/// <summary>
/// Consumes <see cref="BidPlaced"/> events published by the Bidding Service and keeps
/// <c>Auction.CurrentHighBid</c> in sync so the Auction Service stays authoritative for
/// auction state without a synchronous call back into Bidding.
/// </summary>
/// <remarks>
/// Race-safe by construction: the high bid is raised via a single atomic conditional
/// UPDATE (<see cref="IAuctionRepository.TryRaiseHighBidAsync"/>) that only succeeds when
/// the auction is still <c>Live</c> and the message's <see cref="BidPlaced.Amount"/> strictly
/// exceeds the stored high bid. Because the predicate is evaluated by the database in a
/// single statement, concurrent <see cref="BidPlaced"/> messages for the same auction cannot
/// lose updates, and redelivery (or out-of-order delivery) of an older/equal bid is a no-op.
/// </remarks>
public class BidPlacedConsumer(
    IAuctionRepository repository,
    ILogger<BidPlacedConsumer> logger) : IConsumer<BidPlaced>
{
    public async Task Consume(ConsumeContext<BidPlaced> context)
    {
        var message = context.Message;

        // A malformed AuctionId is a poison-message scenario, not a transient failure —
        // log and return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.AuctionId, out var auctionId))
        {
            logger.LogWarning(
                "BidPlaced message had an unparsable AuctionId {AuctionId} — skipping",
                message.AuctionId);
            return;
        }

        // Only "Accepted" and "AcceptedBelowReserve" represent a genuine new high bid;
        // "TooLow"/"Finished" never move CurrentHighBid. Exact (null-safe) matching avoids
        // substring fragility against future status values.
        if (message.BidStatus is not ("Accepted" or "AcceptedBelowReserve"))
        {
            logger.LogDebug(
                "BidPlaced for Auction {AuctionId} was status {BidStatus} — high bid unchanged",
                auctionId,
                message.BidStatus);
            return;
        }

        // Atomic conditional update in the database: race-safe against concurrent bids for
        // the same auction and idempotent against redelivery (the "strictly greater & still
        // Live" predicate is evaluated by Postgres, not in memory).
        var raised = await repository.TryRaiseHighBidAsync(auctionId, message.Amount);

        if (raised)
        {
            // Bidder identity is intentionally never logged.
            logger.LogInformation(
                "Auction {AuctionId} high bid updated to {Amount}", auctionId, message.Amount);
        }
        else
        {
            logger.LogDebug(
                "BidPlaced for Auction {AuctionId} did not raise the high bid — ignored", auctionId);
        }
    }
}
