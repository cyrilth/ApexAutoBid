using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCancelled"/> events published when a seller/admin cancels a
/// still-live auction (<c>POST api/admin/auctions/{id}/cancel</c>) and marks the corresponding
/// search document's <c>Status</c> as <c>"Cancelled"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by construction: once the indexed item's <c>Status</c> has moved off
/// <c>"Live"</c>, it is treated as already finalized and the message is acknowledged without
/// rewriting any field — mirroring <c>AuctionFinishedConsumer</c>'s guard. Redelivery of the
/// same <see cref="AuctionCancelled"/> event is therefore a no-op.
/// </para>
/// <para>
/// <b>Unknown AuctionId is logged and dropped, not retried:</b> unlike
/// <c>AuctionUpdatedConsumer</c>/<c>AuctionFinishedConsumer</c>/<c>BidPlacedConsumer</c>/
/// <c>BidRemovedConsumer</c> (which rethrow a missing item so MassTransit retries, since a
/// not-yet-indexed item there is routine out-of-order delivery worth waiting out — the
/// corresponding <c>AuctionCreated</c> may still be in flight), a missing item here is logged
/// as a warning and the message is simply skipped. A cancellation is an administrative,
/// out-of-band action on an auction that must already exist — there is no plausible
/// in-flight-creation race to wait out, so retrying forever would only ever end in this
/// message stuck in the endpoint's error queue for no productive reason.
/// </para>
/// </remarks>
public class AuctionCancelledConsumer(
    IItemRepository repository,
    ILogger<AuctionCancelledConsumer> logger) : IConsumer<AuctionCancelled>
{
    public async Task Consume(ConsumeContext<AuctionCancelled> context)
    {
        var message = context.Message;

        // A malformed AuctionId is a poison-message scenario, not a transient failure —
        // log and return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.AuctionId, out var auctionId))
        {
            logger.LogWarning(
                "AuctionCancelled message had an unparsable AuctionId {AuctionId} — skipping",
                message.AuctionId);
            return;
        }

        var item = await repository.GetByIdAsync(auctionId, context.CancellationToken);
        if (item is null)
        {
            // See this class's remarks — unlike most of this service's other consumers, a
            // missing item here is not treated as a retry-worthy transient race.
            logger.LogWarning(
                "Item {AuctionId} not found for AuctionCancelled — skipping", auctionId);
            return;
        }

        // Idempotency guard: a Live item has not yet been finalized. Anything else
        // (Finished, ReserveNotMet, Cancelled) means the auction was already finalized by
        // some other event — redelivery, or a late-arriving AuctionCancelled for an auction
        // that has since finished normally, must not rewrite the already-settled outcome.
        if (item.Status != "Live")
        {
            logger.LogInformation(
                "Auction {AuctionId} already finalized with status {Status} — AuctionCancelled ignored",
                auctionId,
                item.Status);
            return;
        }

        item.Status = "Cancelled";
        item.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertAsync(item, context.CancellationToken);

        logger.LogInformation("Auction {AuctionId} cancelled", auctionId);
    }
}
