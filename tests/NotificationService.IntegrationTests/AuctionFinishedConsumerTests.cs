using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 6 Task 6.3 — publishing an <see cref="AuctionFinished"/> event onto the real broker must
/// reach <see cref="NotificationService.Consumers.AuctionFinishedConsumer"/>, which broadcasts it
/// to every connected SignalR client via <c>Clients.All.SendAsync("AuctionFinished", ...)</c>.
/// Targeted <c>AuctionWon</c>/<c>AuctionSellerResult</c> delivery is covered separately by
/// <see cref="TargetedMessageTests"/> (Task 6.5).
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class AuctionFinishedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionFinished_BroadcastsToConnectedClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<AuctionFinished>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<AuctionFinished>("AuctionFinished", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var auctionId = Guid.NewGuid().ToString();
        var expected = new AuctionFinished(
            ItemSold: false,
            AuctionId: auctionId,
            Winner: null,
            WinnerEmail: null,
            Seller: "seller-6-3",
            Amount: null);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(auctionId, received.AuctionId);
        Assert.False(received.ItemSold);
        Assert.Equal("seller-6-3", received.Seller);
    }
}
