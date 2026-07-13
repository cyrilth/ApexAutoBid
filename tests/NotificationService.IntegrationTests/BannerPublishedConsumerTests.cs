using Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 11 Task 6 — publishing a <see cref="BannerPublished"/> event onto the real broker must
/// reach <see cref="NotificationService.Consumers.BannerPublishedConsumer"/>, which broadcasts
/// the full banner payload to every connected SignalR client via
/// <c>Clients.All.SendAsync("BannerPublished", ...)</c>.
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class BannerPublishedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_BannerPublished_BroadcastsToConnectedClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<BannerPublished>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<BannerPublished>("BannerPublished", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var bannerId = Guid.NewGuid();
        var auctionId = Guid.NewGuid().ToString();
        var activeFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var activeUntil = activeFrom.AddDays(7);
        var expected = new BannerPublished(
            bannerId, "Flash sale this weekend!", "Auction", auctionId, activeFrom, activeUntil);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(bannerId, received.Id);
        Assert.Equal("Flash sale this weekend!", received.Message);
        Assert.Equal("Auction", received.Scope);
        Assert.Equal(auctionId, received.AuctionId);
        Assert.Equal(activeFrom, received.ActiveFrom);
        Assert.Equal(activeUntil, received.ActiveUntil);
    }

    [Fact]
    public async Task Publish_BannerPublished_GlobalScope_HasNullAuctionId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        var tcs = new TaskCompletionSource<BannerPublished>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<BannerPublished>("BannerPublished", message => tcs.TrySetResult(message));

        await connection.StartAsync(cancellationToken);

        var bannerId = Guid.NewGuid();
        var activeFrom = DateTime.UtcNow;
        var expected = new BannerPublished(
            bannerId, "Platform-wide maintenance notice", "Global", null, activeFrom, activeFrom.AddHours(1));

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(expected, cancellationToken);

        var received = await SignalRTestHelpers.WaitForMessageAsync(tcs, cancellationToken);

        Assert.Equal(bannerId, received.Id);
        Assert.Equal("Global", received.Scope);
        Assert.Null(received.AuctionId);
    }
}
