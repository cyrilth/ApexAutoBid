using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 6 Task 6.2 — publishing a <see cref="BidPlaced"/> event onto the real broker must reach
/// <see cref="NotificationService.Consumers.BidPlacedConsumer"/>, which broadcasts it to every
/// connected SignalR client via <c>Clients.All.SendAsync("BidPlaced", ...)</c>.
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class BidPlacedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_BidPlaced_BroadcastsToConnectedClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<BidPlaced>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<BidPlaced>("BidPlaced", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var auctionId = Guid.NewGuid().ToString();
        var expected = new BidPlaced(
            Guid.NewGuid().ToString(), auctionId, "bidder-6-2", DateTime.UtcNow, 21000, "Accepted");

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(auctionId, received.AuctionId);
        Assert.Equal("bidder-6-2", received.Bidder);
        Assert.Equal(21000, received.Amount);
        Assert.Equal("Accepted", received.BidStatus);
    }
}
