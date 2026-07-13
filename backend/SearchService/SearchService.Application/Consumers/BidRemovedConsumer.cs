using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="BidRemoved"/> events published by the Bidding Service (a bid
/// retraction/invalidation) and re-syncs the search index's <c>CurrentHighBid</c> to whatever
/// the Bidding Service recomputed it to be after the removal.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent: this is a direct, unconditional assignment of <c>Item.CurrentHighBid</c> from
/// <see cref="BidRemoved.CurrentHighBid"/> — unlike <c>BidPlacedConsumer</c>'s
/// strictly-greater conditional raise, a removal can only ever lower or clear the recorded high
/// bid, never raise it, so there is no "stale lower value overwriting a newer higher one" race
/// to guard against the same way a plain assignment normally would need to. Redelivery of the
/// same event re-applies the same value and is therefore a no-op.
/// <see cref="BidRemoved.CurrentHighBid"/> is <see langword="null"/> when no bids remain on the
/// auction after the removal, mirroring <see cref="SearchService.Domain.Entities.Item.CurrentHighBid"/>'s
/// own nullable representation — the value is assigned as-is, with no zero/null coercion.
/// </para>
/// <para>
/// <b>Transient vs. poison:</b> an unparsable <see cref="BidRemoved.AuctionId"/> is garbage
/// that will never parse no matter how many times it's redelivered — a poison message, logged
/// and dropped. An item that isn't found in the index is different: in this service, not-found
/// is a routine out-of-order race (the corresponding <c>AuctionCreated</c> may still be in
/// flight), not an anomaly — so it is logged and <b>rethrown</b>, matching
/// <c>BidPlacedConsumer</c>'s classification, so the bus retry policy applies and only a
/// persistently-missing item lands in the endpoint's _error queue.
/// </para>
/// </remarks>
public class BidRemovedConsumer(
    IItemRepository repository,
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

        var item = await repository.GetByIdAsync(auctionId, context.CancellationToken);
        if (item is null)
        {
            // Transient out-of-order delivery (AuctionCreated hasn't landed yet), not a
            // poison message — throw so MassTransit retries/faults instead of silently
            // dropping a real high-bid correction.
            logger.LogWarning(
                "Item {AuctionId} not found for BidRemoved — will retry", auctionId);
            throw new InvalidOperationException(
                $"Item {auctionId} not found for BidRemoved.");
        }

        item.CurrentHighBid = message.CurrentHighBid;
        item.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertAsync(item, context.CancellationToken);

        logger.LogInformation(
            "Auction {AuctionId} high bid updated to {CurrentHighBid} after bid removal",
            auctionId,
            message.CurrentHighBid);
    }
}
