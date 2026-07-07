using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 6 Task 6.1 — publishing an <see cref="AuctionCreated"/> event onto the real broker must
/// reach <see cref="NotificationService.Consumers.AuctionCreatedConsumer"/>, which broadcasts it
/// to every connected SignalR client via <c>Clients.All.SendAsync("AuctionCreated", ...)</c>.
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class AuctionCreatedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionCreated_BroadcastsToConnectedClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<AuctionCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<AuctionCreated>("AuctionCreated", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var auctionEnd = createdAt.AddDays(7);
        var expected = new AuctionCreated(
            id, createdAt, createdAt, auctionEnd, "carol-6-1", "", "Ford", "GT", 2020, "Red",
            12345, "http://images.local/ford-gt.jpg", "http://images.local/ford-gt-thumb.jpg",
            "Live", 20000, null, null);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(id, received.Id);
        Assert.Equal("carol-6-1", received.Seller);
        Assert.Equal("Ford", received.Make);
        Assert.Equal("GT", received.Model);
        Assert.Equal(20000, received.ReservePrice);
    }
}
