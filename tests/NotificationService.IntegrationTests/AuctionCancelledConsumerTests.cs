using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 11 Task 6 — publishing an <see cref="AuctionCancelled"/> event onto the real broker
/// must reach <see cref="NotificationService.Consumers.AuctionCancelledConsumer"/>, which both
/// broadcasts it to every connected SignalR client via
/// <c>Clients.All.SendAsync("AuctionCancelled", ...)</c> AND sends a targeted follow-up to the
/// seller's connection via <c>Clients.User(Seller).SendAsync("AuctionCancelledForSeller", ...)</c>,
/// mirroring <see cref="AuctionFinishedConsumerTests"/>/<see cref="TargetedMessageTests"/>'s
/// broadcast + targeted coverage for <c>AuctionFinished</c>.
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class AuctionCancelledConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionCancelled_BroadcastsToConnectedClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<AuctionCancelled>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<AuctionCancelled>("AuctionCancelled", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var auctionId = Guid.NewGuid().ToString();
        var expected = new AuctionCancelled(auctionId, "seller-11-6-broadcast");

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(auctionId, received.AuctionId);
        Assert.Equal("seller-11-6-broadcast", received.Seller);
    }

    [Fact]
    public async Task Publish_AuctionCancelled_SendsTargetedMessageToSellerOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var sellerUsername = $"seller-11-6-{Guid.NewGuid():N}";
        var auctionId = Guid.NewGuid().ToString();

        await using var sellerConnection = SignalRTestHelpers.CreateConnection(factory, sellerUsername);
        await using var anonymousConnection = SignalRTestHelpers.CreateConnection(factory);

        var sellerBroadcastTcs = NewTcs();
        var sellerTargetedTcs = NewTcs();
        sellerConnection.On<AuctionCancelled>("AuctionCancelled", m => sellerBroadcastTcs.TrySetResult(m));
        sellerConnection.On<AuctionCancelled>("AuctionCancelledForSeller", m => sellerTargetedTcs.TrySetResult(m));

        var anonymousBroadcastTcs = NewTcs();
        var anonymousTargetedTcs = NewTcs();
        anonymousConnection.On<AuctionCancelled>("AuctionCancelled", m => anonymousBroadcastTcs.TrySetResult(m));
        anonymousConnection.On<AuctionCancelled>("AuctionCancelledForSeller", m => anonymousTargetedTcs.TrySetResult(m));

        await sellerConnection.StartAsync(cancellationToken);
        await anonymousConnection.StartAsync(cancellationToken);

        var expected = new AuctionCancelled(auctionId, sellerUsername);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        // Every connected client — authenticated or not — receives the broadcast.
        var sellerBroadcast = await SignalRTestHelpers.WaitForMessageAsync(sellerBroadcastTcs, cancellationToken);
        var anonymousBroadcast = await SignalRTestHelpers.WaitForMessageAsync(anonymousBroadcastTcs, cancellationToken);
        Assert.Equal(auctionId, sellerBroadcast.AuctionId);
        Assert.Equal(auctionId, anonymousBroadcast.AuctionId);

        // Only the seller's connection receives the targeted AuctionCancelledForSeller message.
        var targeted = await SignalRTestHelpers.WaitForMessageAsync(sellerTargetedTcs, cancellationToken);
        Assert.Equal(auctionId, targeted.AuctionId);
        Assert.Equal(sellerUsername, targeted.Seller);

        // The anonymous connection must never receive the targeted message — only the broadcast.
        await SignalRTestHelpers.AssertNoMessageAsync(anonymousTargetedTcs, cancellationToken);

        return;

        static TaskCompletionSource<AuctionCancelled> NewTcs() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
