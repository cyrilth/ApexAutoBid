using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionDeleted"/> events published by the Auction Service and
/// removes the corresponding document from the search index.
/// </summary>
/// <remarks>
/// Idempotent: <see cref="IItemRepository.DeleteAsync"/> succeeds silently when the id no
/// longer exists, so redelivery of the same <see cref="AuctionDeleted"/> event (or delivery
/// after the item was already removed) is a safe no-op.
/// </remarks>
public class AuctionDeletedConsumer(
    IItemRepository repository,
    ILogger<AuctionDeletedConsumer> logger) : IConsumer<AuctionDeleted>
{
    public async Task Consume(ConsumeContext<AuctionDeleted> context)
    {
        var message = context.Message;

        // A malformed Id is a poison-message scenario, not a transient failure — log and
        // return rather than throwing, so the broker does not redeliver it forever.
        if (!Guid.TryParse(message.Id, out var auctionId))
        {
            logger.LogWarning(
                "AuctionDeleted message had an unparsable Id {AuctionId} — skipping",
                message.Id);
            return;
        }

        await repository.DeleteAsync(auctionId, context.CancellationToken);

        logger.LogInformation("Removed auction {AuctionId} from the search index", auctionId);
    }
}
