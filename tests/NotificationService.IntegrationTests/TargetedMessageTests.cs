using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 6 Task 6.5 — targeted, per-user delivery of <c>AuctionWon</c>/
/// <c>AuctionSellerResult</c>, on top of the <c>AuctionFinished</c> broadcast every connected
/// client receives regardless of authentication (Task 6.3, covered separately by
/// <see cref="AuctionFinishedConsumerTests"/>).
/// </summary>
/// <remarks>
/// Three connections are used, mirroring <see cref="NotificationService.Consumers.AuctionFinishedConsumer"/>'s
/// own three audiences: a client authenticated as the winner (reachable via
/// <c>Clients.User(Winner)</c>), a client authenticated as the seller (reachable via
/// <c>Clients.User(Seller)</c>), and a genuinely anonymous client, which
/// <see cref="NotificationService.Hubs.UsernameUserIdProvider"/>'s remarks say is never
/// reachable via <c>Clients.User</c> at all — it must observe only the broadcast.
/// </remarks>
[Collection(NotificationServiceCollection.Name)]
public class TargetedMessageTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionFinished_ItemSold_SendsTargetedMessagesToWinnerAndSellerOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var winnerUsername = $"winner-6-5-{Guid.NewGuid():N}";
        var sellerUsername = $"seller-6-5-{Guid.NewGuid():N}";
        var auctionId = Guid.NewGuid().ToString();

        await using var winnerConnection = SignalRTestHelpers.CreateConnection(factory, winnerUsername);
        await using var sellerConnection = SignalRTestHelpers.CreateConnection(factory, sellerUsername);
        await using var anonymousConnection = SignalRTestHelpers.CreateConnection(factory);

        var winnerBroadcastTcs = NewTcs();
        var winnerWonTcs = NewTcs();
        var winnerSellerResultTcs = NewTcs();
        winnerConnection.On<AuctionFinished>("AuctionFinished", m => winnerBroadcastTcs.TrySetResult(m));
        winnerConnection.On<AuctionFinished>("AuctionWon", m => winnerWonTcs.TrySetResult(m));
        winnerConnection.On<AuctionFinished>("AuctionSellerResult", m => winnerSellerResultTcs.TrySetResult(m));

        var sellerBroadcastTcs = NewTcs();
        var sellerWonTcs = NewTcs();
        var sellerSellerResultTcs = NewTcs();
        sellerConnection.On<AuctionFinished>("AuctionFinished", m => sellerBroadcastTcs.TrySetResult(m));
        sellerConnection.On<AuctionFinished>("AuctionWon", m => sellerWonTcs.TrySetResult(m));
        sellerConnection.On<AuctionFinished>("AuctionSellerResult", m => sellerSellerResultTcs.TrySetResult(m));

        var anonymousBroadcastTcs = NewTcs();
        var anonymousWonTcs = NewTcs();
        var anonymousSellerResultTcs = NewTcs();
        anonymousConnection.On<AuctionFinished>("AuctionFinished", m => anonymousBroadcastTcs.TrySetResult(m));
        anonymousConnection.On<AuctionFinished>("AuctionWon", m => anonymousWonTcs.TrySetResult(m));
        anonymousConnection.On<AuctionFinished>("AuctionSellerResult", m => anonymousSellerResultTcs.TrySetResult(m));

        await winnerConnection.StartAsync(cancellationToken);
        await sellerConnection.StartAsync(cancellationToken);
        await anonymousConnection.StartAsync(cancellationToken);

        var expected = new AuctionFinished(
            ItemSold: true,
            AuctionId: auctionId,
            Winner: winnerUsername,
            WinnerEmail: "winner@apexautobid.local",
            Seller: sellerUsername,
            Amount: 25000);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        // Every connected client — authenticated or not — receives the broadcast.
        var winnerBroadcast = await SignalRTestHelpers.WaitForMessageAsync(winnerBroadcastTcs, cancellationToken);
        var sellerBroadcast = await SignalRTestHelpers.WaitForMessageAsync(sellerBroadcastTcs, cancellationToken);
        var anonymousBroadcast = await SignalRTestHelpers.WaitForMessageAsync(anonymousBroadcastTcs, cancellationToken);
        Assert.Equal(auctionId, winnerBroadcast.AuctionId);
        Assert.Equal(auctionId, sellerBroadcast.AuctionId);
        Assert.Equal(auctionId, anonymousBroadcast.AuctionId);

        // Only the winner's connection receives the targeted AuctionWon message.
        var won = await SignalRTestHelpers.WaitForMessageAsync(winnerWonTcs, cancellationToken);
        Assert.Equal(auctionId, won.AuctionId);
        Assert.Equal(winnerUsername, won.Winner);

        // Only the seller's connection receives the targeted AuctionSellerResult message.
        var sellerResult = await SignalRTestHelpers.WaitForMessageAsync(sellerSellerResultTcs, cancellationToken);
        Assert.Equal(auctionId, sellerResult.AuctionId);
        Assert.Equal(sellerUsername, sellerResult.Seller);

        // Cross-checks: the seller's connection must NOT receive AuctionWon, the winner's
        // connection must NOT receive AuctionSellerResult, and the anonymous connection must
        // receive NEITHER targeted message — only the broadcast asserted above.
        await SignalRTestHelpers.AssertNoMessageAsync(sellerWonTcs, cancellationToken);
        await SignalRTestHelpers.AssertNoMessageAsync(winnerSellerResultTcs, cancellationToken);
        await SignalRTestHelpers.AssertNoMessageAsync(anonymousWonTcs, cancellationToken);
        await SignalRTestHelpers.AssertNoMessageAsync(anonymousSellerResultTcs, cancellationToken);

        return;

        static TaskCompletionSource<AuctionFinished> NewTcs() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
