using Contracts;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Logging;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCreated"/> events published by the Auction Service and
/// indexes the new auction in the search store.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by construction: <see cref="IItemRepository.UpsertAsync"/> is a full
/// insert-or-replace keyed on the auction's own Guid (<c>ItemDocument._id</c> — see
/// <c>ItemDocument</c>'s XML doc), so redelivery of the same <see cref="AuctionCreated"/>
/// event overwrites the document with identical values rather than producing a duplicate.
/// </para>
/// <para>
/// <b>Known limitation — delete/create resurrection window:</b> if an <c>AuctionDeleted</c>
/// for a given id is processed <i>before</i> its <c>AuctionCreated</c> (retry-induced
/// message reordering across the two queues), the deletion runs first against a
/// not-yet-indexed item (a no-op — see <c>AuctionDeletedConsumer</c>), and this consumer's
/// later upsert then re-inserts the item with nothing left to remove it again. This service
/// deliberately does not maintain tombstone records to close that window (it would mean
/// permanently retaining a marker for every deleted auction). The Phase 2 Task 6 HTTP
/// polling reconciliation against the Auction Service is the backstop that eventually
/// corrects any index entry left resurrected this way.
/// </para>
/// </remarks>
public class AuctionCreatedConsumer(
    IItemRepository repository,
    IMapper mapper,
    ILogger<AuctionCreatedConsumer> logger) : IConsumer<AuctionCreated>
{
    public async Task Consume(ConsumeContext<AuctionCreated> context)
    {
        var message = context.Message;

        // ItemMappingConfig normalizes Winner: AuctionService always publishes "" for a
        // brand-new auction; Item.Winner is string? and must be null instead.
        var item = mapper.Map<Item>(message);

        await repository.UpsertAsync(item, context.CancellationToken);

        logger.LogInformation("Indexed new auction {AuctionId}", message.Id);
    }
}
