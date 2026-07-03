using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionFinished"/> events published by the Bidding Service and
/// finalizes the search document's <c>Status</c>, <c>Winner</c>, and <c>SoldAmount</c>.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by construction: once the indexed item's <c>Status</c> has moved off
/// <c>"Live"</c>, it is treated as already finalized and the message is acknowledged
/// without rewriting any field — mirroring
/// <c>AuctionService.Application.Consumers.AuctionFinishedConsumer</c>'s guard. Redelivery
/// of the same <see cref="AuctionFinished"/> event is therefore a no-op.
/// </para>
/// <para>
/// <b>Privacy:</b> <see cref="AuctionFinished.WinnerEmail"/> is never mapped, stored, or
/// logged here — <c>SearchService.Domain.Entities.Item</c> has no email field by design
/// (Requirements §3.1 / Item's own XML doc). <see cref="AuctionFinished.Winner"/> is
/// normalized the same way as <c>AuctionCreated.Winner</c> (empty collapses to null) in
/// case an empty-string sentinel is ever published for a no-sale outcome.
/// </para>
/// <para>
/// <b>Transient vs. poison:</b> an unparsable <see cref="AuctionFinished.AuctionId"/> is
/// garbage that will never parse no matter how many times it's redelivered — a poison
/// message, logged and dropped. An item that isn't found in the index is different: in this
/// service, not-found is a routine out-of-order race (the corresponding <c>AuctionCreated</c>
/// may still be in flight), not an anomaly — so it is logged and <b>rethrown</b>, matching
/// <c>AuctionUpdatedConsumer</c>'s classification, so the bus retry policy applies and only a
/// persistently-missing item lands in the endpoint's _error queue.
/// </para>
/// </remarks>
public class AuctionFinishedConsumer(
    IItemRepository repository,
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

        var item = await repository.GetByIdAsync(auctionId, context.CancellationToken);
        if (item is null)
        {
            // Transient out-of-order delivery (AuctionCreated hasn't landed yet), not a
            // poison message — throw so MassTransit retries/faults instead of silently
            // dropping the finalization.
            logger.LogWarning(
                "Item {AuctionId} not found for AuctionFinished — will retry", auctionId);
            throw new InvalidOperationException(
                $"Item {auctionId} not found for AuctionFinished.");
        }

        // Idempotency guard: a Live item has not yet been finalized. Anything else
        // (Finished, ReserveNotMet, Cancelled) means this event was already applied —
        // redelivery must not rewrite the already-settled outcome.
        if (item.Status != "Live")
        {
            logger.LogInformation(
                "Auction {AuctionId} already finalized with status {Status} — AuctionFinished ignored",
                auctionId,
                item.Status);
            return;
        }

        if (message.ItemSold)
        {
            item.Winner = string.IsNullOrEmpty(message.Winner) ? null : message.Winner;
            item.SoldAmount = message.Amount;
        }

        item.Status = message.ItemSold ? "Finished" : "ReserveNotMet";
        item.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertAsync(item, context.CancellationToken);

        // Winner is intentionally not logged (identity); WinnerEmail is never referenced.
        logger.LogInformation(
            "Auction {AuctionId} finalized with status {Status}", auctionId, item.Status);
    }
}
