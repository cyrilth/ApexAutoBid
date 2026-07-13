using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BiddingService.Application.Consumers;

/// <summary>
/// Consumes <see cref="AuctionUpdated"/> (Phase 11 Task 5.3 / Requirements §10.2) — applies
/// <see cref="AuctionUpdated.AuctionEnd"/> to the local <see cref="Domain.Entities.Auction"/>
/// projection when present. This is how an admin's "end now"
/// (<c>POST api/admin/auctions/{id}/end</c>, which sets <c>AuctionEnd = UtcNow</c> in the
/// Auction Service and re-emits <see cref="AuctionUpdated"/> with that new value) reaches this
/// service's own background finalizer — the very next tick sees the updated
/// <c>AuctionEnd</c> via <c>IAuctionRepository.GetExpiredUnfinalizedAsync</c> and finalizes it
/// through the normal flow. An ordinary seller's own edit to <c>AuctionEnd</c> (within the
/// platform's duration limits) flows through the exact same event/consumer.
/// </summary>
/// <remarks>
/// <see cref="Domain.Entities.Auction"/> only ever needs <c>AuctionEnd</c> from this event — every
/// other <see cref="AuctionUpdated"/> field (Make/Model/Color/Mileage/Year/ImageUrl/ThumbnailUrl)
/// describes item details this service's minimal local projection deliberately does not carry
/// (see <c>Auction</c>'s own remarks) and is therefore ignored here. A <see langword="null"/>
/// <c>AuctionEnd</c> means the update didn't touch it — nothing to apply, a genuine no-op, not
/// an error. Idempotent: redelivery of the same event sets the same value again.
/// </remarks>
public class AuctionUpdatedConsumer(
    IAuctionRepository repository,
    ILogger<AuctionUpdatedConsumer> logger) : IConsumer<AuctionUpdated>
{
    public async Task Consume(ConsumeContext<AuctionUpdated> context)
    {
        var message = context.Message;

        if (message.AuctionEnd is null)
        {
            logger.LogDebug(
                "AuctionUpdated for auction {AuctionId} carried no AuctionEnd change — nothing to apply locally",
                message.Id);
            return;
        }

        var auctionId = Guid.Parse(message.Id);

        await repository.UpdateAuctionEndAsync(auctionId, message.AuctionEnd.Value, context.CancellationToken);

        logger.LogInformation(
            "Updated local AuctionEnd for auction {AuctionId} to {AuctionEnd}",
            auctionId, message.AuctionEnd);
    }
}
