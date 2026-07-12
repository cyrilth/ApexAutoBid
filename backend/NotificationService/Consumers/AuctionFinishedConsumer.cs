using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Consumers;

/// <summary>
/// Consumes <see cref="AuctionFinished"/> events published by the Bidding Service and both
/// broadcasts the result to every connected client and, when the relevant party is
/// authenticated, sends targeted follow-up messages to the winner and the seller (Phase 6
/// Tasks 4.3/4.4 / Architecture.md §3.3).
/// </summary>
/// <remarks>
/// <para>
/// <c>Winner</c> and <c>Seller</c> on <see cref="AuctionFinished"/> are usernames (Contracts/
/// AuctionFinished.cs), matching <see cref="UsernameUserIdProvider"/>'s username-based
/// <c>Clients.User(...)</c> mapping — an authenticated SignalR connection for that username
/// receives the targeted message; an anonymous connection (or no connection for that user at
/// all) simply never gets it, receiving only the broadcast above.
/// </para>
/// <para>
/// <c>WinnerEmail</c> is never sent over the hub — Architecture.md §3.3 is explicit that the
/// post-sale email exchange goes through <c>GET api/auctions/{id}</c> only, never SignalR
/// (Requirements.md §13.5 also forbids logging/pushing email addresses outside that flow).
/// Enforced below by sending a redacted copy (<c>WinnerEmail = null</c>) on every hub send,
/// broadcast and targeted alike.
/// </para>
/// <para>
/// Idempotent by construction — see <see cref="AuctionCreatedConsumer"/>'s identical remark;
/// there is no local state here for a redelivered event to corrupt.
/// </para>
/// </remarks>
public class AuctionFinishedConsumer(
    IHubContext<NotificationHub> hub,
    ILogger<AuctionFinishedConsumer> logger) : IConsumer<AuctionFinished>
{
    public async Task Consume(ConsumeContext<AuctionFinished> context)
    {
        var message = context.Message;

        // Redacted copy for ALL hub sends — WinnerEmail must never reach a browser via
        // SignalR (see remarks). The winner and seller obtain the post-sale email through
        // GET api/auctions/{id}, where AuctionService authorizes the caller per-field.
        var redacted = message with { WinnerEmail = null };

        await hub.Clients.All.SendAsync("AuctionFinished", redacted, context.CancellationToken);

        logger.LogInformation("Broadcast AuctionFinished for auction {AuctionId}", message.AuctionId);

        if (message.ItemSold && !string.IsNullOrEmpty(message.Winner))
        {
            await hub.Clients.User(message.Winner)
                .SendAsync("AuctionWon", redacted, context.CancellationToken);

            logger.LogInformation("Sent targeted AuctionWon for auction {AuctionId}", message.AuctionId);
        }

        await hub.Clients.User(message.Seller)
            .SendAsync("AuctionSellerResult", redacted, context.CancellationToken);

        logger.LogInformation(
            "Sent targeted AuctionSellerResult for auction {AuctionId}", message.AuctionId);
    }
}
