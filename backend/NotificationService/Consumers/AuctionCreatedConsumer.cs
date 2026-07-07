using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCreated"/> events published by the Auction Service and
/// broadcasts them to every connected SignalR client (Phase 6 Task 4.1 / Architecture.md
/// §3.3) so the frontend can show a new auction without polling.
/// </summary>
/// <remarks>
/// Idempotent by construction: pushing the same event twice (e.g. on redelivery after a
/// transient consumer failure) is naturally safe here — there is no local state to corrupt,
/// only a duplicate client-side notification, which the frontend can de-duplicate by the
/// event's own <c>Id</c> if that ever matters.
/// </remarks>
public class AuctionCreatedConsumer(
    IHubContext<NotificationHub> hub,
    ILogger<AuctionCreatedConsumer> logger) : IConsumer<AuctionCreated>
{
    public async Task Consume(ConsumeContext<AuctionCreated> context)
    {
        var message = context.Message;

        await hub.Clients.All.SendAsync("AuctionCreated", message, context.CancellationToken);

        logger.LogInformation("Broadcast AuctionCreated for auction {AuctionId}", message.Id);
    }
}
