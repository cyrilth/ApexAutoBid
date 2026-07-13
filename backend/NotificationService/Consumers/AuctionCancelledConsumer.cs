using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Consumers;

/// <summary>
/// Consumes <see cref="AuctionCancelled"/> events published by the Auction Service (admin
/// cancellation — Docs/Tasks.md Phase 11 Task 3.3) and both broadcasts the cancellation to every
/// connected SignalR client and sends a targeted follow-up to the seller, mirroring
/// <see cref="AuctionFinishedConsumer"/>'s broadcast + <c>Clients.User(...)</c> pattern
/// (Architecture.md §3.3 / Docs/Tasks.md Phase 11 Task 6).
/// </summary>
/// <remarks>
/// <para>
/// <c>Seller</c> on <see cref="AuctionCancelled"/> is a username (Contracts/AuctionCancelled.cs),
/// matching <see cref="UsernameUserIdProvider"/>'s username-based <c>Clients.User(...)</c>
/// mapping — an authenticated SignalR connection for that username receives the targeted
/// <c>"AuctionCancelledForSeller"</c> message; an anonymous connection (or no connection for
/// that user at all) simply never gets it, receiving only the <c>"AuctionCancelled"</c>
/// broadcast above.
/// </para>
/// <para>
/// Idempotent by construction — see <see cref="AuctionCreatedConsumer"/>'s identical remark;
/// there is no local state here for a redelivered event to corrupt. This is pure push: a
/// redelivery after a transient consumer failure just re-broadcasts/re-sends the same
/// notification, which is acceptable — at most a duplicate toast, never a duplicate side effect.
/// </para>
/// </remarks>
public class AuctionCancelledConsumer(
    IHubContext<NotificationHub> hub,
    ILogger<AuctionCancelledConsumer> logger) : IConsumer<AuctionCancelled>
{
    public async Task Consume(ConsumeContext<AuctionCancelled> context)
    {
        var message = context.Message;

        await hub.Clients.All.SendAsync("AuctionCancelled", message, context.CancellationToken);

        logger.LogInformation("Broadcast AuctionCancelled for auction {AuctionId}", message.AuctionId);

        await hub.Clients.User(message.Seller)
            .SendAsync("AuctionCancelledForSeller", message, context.CancellationToken);

        logger.LogInformation(
            "Sent targeted AuctionCancelledForSeller for auction {AuctionId}", message.AuctionId);
    }
}
