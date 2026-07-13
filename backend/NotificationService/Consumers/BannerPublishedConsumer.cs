using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Consumers;

/// <summary>
/// Consumes <see cref="BannerPublished"/> events published by the Auction Service (admin banner
/// CRUD — Docs/Tasks.md Phase 11 Task 3.5) and broadcasts the full banner payload to every
/// connected SignalR client (Architecture.md §3.3 / Docs/Tasks.md Phase 11 Task 6) so an open
/// home page or auction detail page can show/refresh the banner without a reload.
/// </summary>
/// <remarks>
/// <para>
/// Broadcast only — a banner is never seller/user-specific (unlike
/// <see cref="AuctionCancelledConsumer"/>/<see cref="AuctionFinishedConsumer"/>'s additional
/// targeted sends), so every connection, authenticated or anonymous, receives the same message;
/// the frontend is expected to filter by <c>Scope</c>/<c>AuctionId</c> itself to decide where to
/// render it (global vs. home page vs. a specific auction's detail page).
/// </para>
/// <para>
/// Idempotent by construction — see <see cref="AuctionCreatedConsumer"/>'s identical remark;
/// there is no local state here for a redelivered event to corrupt. This is pure push: a
/// redelivery after a transient consumer failure just re-broadcasts the same banner, which is
/// acceptable.
/// </para>
/// </remarks>
public class BannerPublishedConsumer(
    IHubContext<NotificationHub> hub,
    ILogger<BannerPublishedConsumer> logger) : IConsumer<BannerPublished>
{
    public async Task Consume(ConsumeContext<BannerPublished> context)
    {
        var message = context.Message;

        await hub.Clients.All.SendAsync("BannerPublished", message, context.CancellationToken);

        logger.LogInformation("Broadcast BannerPublished for banner {BannerId}", message.Id);
    }
}
