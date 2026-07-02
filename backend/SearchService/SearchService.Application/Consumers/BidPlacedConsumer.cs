using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Enums;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="BidPlaced"/> events published by the Bidding Service and keeps the
/// search index's <c>CurrentHighBid</c> in sync, mirroring
/// <c>AuctionService.Application.Consumers.BidPlacedConsumer</c>'s accepted-bid semantics so
/// both services agree on what counts as a genuine new high bid.
/// </summary>
/// <remarks>
/// <para>
/// Race-safe by construction: the high bid is raised via a single atomic conditional update
/// (<see cref="IItemRepository.TryRaiseHighBidAsync"/>) that only succeeds when the item is
/// still <c>"Live"</c> and the message's <see cref="BidPlaced.Amount"/> strictly exceeds the
/// stored high bid. Because the predicate is evaluated by MongoDB in a single statement,
/// concurrent <see cref="BidPlaced"/> messages for the same item cannot lose updates, and
/// redelivery (or out-of-order delivery) of an older/equal bid is a no-op.
/// </para>
/// <para>
/// <b>Transient vs. poison:</b> an unparsable <see cref="BidPlaced.AuctionId"/> is garbage
/// that will never parse no matter how many times it's redelivered — a poison message,
/// logged and dropped. A <see cref="HighBidUpdateResult.ItemNotFound"/> result is different:
/// in this service, not-found is a routine out-of-order race (the corresponding
/// <c>AuctionCreated</c> may still be in flight), not an anomaly — so it is logged and
/// <b>rethrown</b>, matching <c>AuctionUpdatedConsumer</c>'s classification, so the bus retry
/// policy applies and only a persistently-missing item lands in the endpoint's _error queue.
/// </para>
/// </remarks>
public class BidPlacedConsumer(
    IItemRepository repository,
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

        // Atomic conditional update in MongoDB: race-safe against concurrent bids for the
        // same item and idempotent against redelivery (the "strictly greater & still Live"
        // predicate is evaluated by the database, not in memory).
        var result = await repository.TryRaiseHighBidAsync(
            auctionId, message.Amount, context.CancellationToken);

        switch (result)
        {
            case HighBidUpdateResult.Raised:
                // Bidder identity is intentionally never logged.
                logger.LogInformation(
                    "Auction {AuctionId} high bid updated to {Amount}", auctionId, message.Amount);
                break;

            case HighBidUpdateResult.ItemNotFound:
                // Transient out-of-order delivery (AuctionCreated hasn't landed yet), not a
                // poison message — throw so MassTransit retries/faults instead of silently
                // dropping a real bid update.
                logger.LogWarning(
                    "Item {AuctionId} not found for BidPlaced — will retry", auctionId);
                throw new InvalidOperationException(
                    $"Item {auctionId} not found for BidPlaced.");

            default: // HighBidUpdateResult.NotRaised
                logger.LogDebug(
                    "BidPlaced for Auction {AuctionId} did not raise the high bid — ignored", auctionId);
                break;
        }
    }
}
