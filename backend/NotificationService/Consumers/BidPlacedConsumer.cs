using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Consumers;

/// <summary>
/// Consumes <see cref="BidPlaced"/> events published by the Bidding Service and broadcasts
/// them to every connected SignalR client (Phase 6 Task 4.2 / Architecture.md §3.3) so bid
/// activity is visible in real time on the auction detail page.
/// </summary>
/// <remarks>
/// Idempotent by construction — see <see cref="AuctionCreatedConsumer"/>'s identical remark;
/// there is no local state here for a redelivered event to corrupt.
/// </remarks>
public class BidPlacedConsumer(
    IHubContext<NotificationHub> hub,
    ILogger<BidPlacedConsumer> logger) : IConsumer<BidPlaced>
{
    public async Task Consume(ConsumeContext<BidPlaced> context)
    {
        var message = context.Message;

        await hub.Clients.All.SendAsync("BidPlaced", message, context.CancellationToken);

        logger.LogInformation("Broadcast BidPlaced for auction {AuctionId}", message.AuctionId);
    }
}
