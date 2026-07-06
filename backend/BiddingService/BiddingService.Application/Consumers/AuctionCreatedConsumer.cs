using BiddingService.Domain.Interfaces;
using Contracts;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Logging;
using Auction = BiddingService.Domain.Entities.Auction;

namespace BiddingService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCreated"/> events published by the Auction Service and stores a
/// minimal local <see cref="Auction"/> projection (Architecture.md §4.2) so bid placement can
/// validate against it without a synchronous call back to the Auction Service.
/// </summary>
/// <remarks>
/// Idempotent by construction: <see cref="IAuctionRepository.InsertIfNotExistsAsync"/> only
/// ever inserts, keyed on the auction's own Guid — redelivery of the same
/// <see cref="AuctionCreated"/> event is a genuine no-op against an already-stored record
/// (phase-end code review Warning 3), rather than resetting anything (e.g. an already-Finished
/// auction being un-finalized) back to the event's own, necessarily creation-time values.
/// </remarks>
public class AuctionCreatedConsumer(
    IAuctionRepository repository,
    IMapper mapper,
    ILogger<AuctionCreatedConsumer> logger) : IConsumer<AuctionCreated>
{
    public async Task Consume(ConsumeContext<AuctionCreated> context)
    {
        var message = context.Message;

        var auction = mapper.Map<Auction>(message);

        await repository.InsertIfNotExistsAsync(auction, context.CancellationToken);

        logger.LogInformation("Stored local auction record for {AuctionId}", message.Id);
    }
}
