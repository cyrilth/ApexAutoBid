using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionUpdated"/> events published by the Auction Service and
/// applies the changed Item-level fields (Make, Model, Color, Mileage, Year, image URLs,
/// AuctionEnd) to the corresponding search document.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent: re-applying the same field values on redelivery produces the same document.
/// Unlike a malformed <see cref="AuctionUpdated.Id"/> (a poison message — logged and
/// dropped, matching the rest of this service's consumers), an update for an auction id
/// this service has never indexed is treated as transient out-of-order delivery (the
/// corresponding <c>AuctionCreated</c> has not been processed yet) and is rethrown so
/// MassTransit retries/faults the message instead of silently dropping a real update.
/// </para>
/// <para>
/// <b>Known limitation — lost updates under reordering:</b> the read-modify-write here
/// (<see cref="IItemRepository.GetByIdAsync"/> then <see cref="IItemRepository.UpsertAsync"/>)
/// is not atomic, and <see cref="AuctionUpdated"/> carries no version number or timestamp to
/// detect staleness. If two distinct-content updates for the same auction are redelivered
/// or processed out of order, the later write wins regardless of which one was published
/// more recently, so a stale set of field values can end up persisted. This is accepted as
/// an eventual-consistency limitation of the read model — adding a version/timestamp is a
/// contract change and out of scope here.
/// </para>
/// </remarks>
public class AuctionUpdatedConsumer(
    IItemRepository repository,
    ILogger<AuctionUpdatedConsumer> logger) : IConsumer<AuctionUpdated>
{
    public async Task Consume(ConsumeContext<AuctionUpdated> context)
    {
        var message = context.Message;

        // A malformed Id is a poison-message scenario, not a transient failure — log and
        // return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.Id, out var auctionId))
        {
            logger.LogWarning(
                "AuctionUpdated message had an unparsable Id {AuctionId} — skipping",
                message.Id);
            return;
        }

        var item = await repository.GetByIdAsync(auctionId, context.CancellationToken);
        if (item is null)
        {
            // Out-of-order delivery (AuctionCreated hasn't landed yet) is transient —
            // throwing lets MassTransit retry/fault instead of silently dropping a real
            // update.
            logger.LogWarning(
                "Item {AuctionId} not found for AuctionUpdated — will retry", auctionId);
            throw new InvalidOperationException(
                $"Item {auctionId} not found for AuctionUpdated.");
        }

        item.Make = message.Make;
        item.Model = message.Model;
        item.Color = message.Color;
        item.Mileage = message.Mileage;
        item.Year = message.Year;
        item.ImageUrl = message.ImageUrl;
        item.ThumbnailUrl = message.ThumbnailUrl;

        // AuctionEnd is nullable on the contract but AuctionService's AuctionDto → AuctionUpdated
        // mapping always populates it (an implicit DateTime → DateTime? widening) — the null
        // check is kept anyway for contract-nullability honesty, not because null is expected.
        if (message.AuctionEnd.HasValue)
            item.AuctionEnd = message.AuctionEnd.Value;

        item.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertAsync(item, context.CancellationToken);

        logger.LogInformation("Updated indexed auction {AuctionId}", auctionId);
    }
}
