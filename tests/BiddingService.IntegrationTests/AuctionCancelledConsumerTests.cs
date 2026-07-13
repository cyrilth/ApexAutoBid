using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BiddingService.Application.DTOs;
using BiddingService.Application.Services;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for consuming <c>AuctionCancelled</c> (Phase 11 Task 5.2), exercising the
/// full real broker + real Mongo pipeline: the local auction projection is marked finished,
/// further <c>POST api/bids</c> attempts are refused with the same shape a normally-ended
/// auction already produces, and the background finalizer never finalizes it even once
/// <c>AuctionEnd</c> has passed. Mirrors <c>AuctionCreatedConsumerTests</c>' identical shape.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class AuctionCancelledConsumerTests(CustomWebAppFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateClient(string? asUser, bool? emailVerified = null)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
        if (emailVerified is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailVerifiedHeader, emailVerified.Value ? "true" : "false");
        return client;
    }

    private async Task<Guid> CreateLiveAuctionAsync(string seller, DateTime auctionEnd)
    {
        var auctionId = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(
            new AuctionCreated(
                auctionId, DateTime.UtcNow, DateTime.UtcNow, auctionEnd,
                seller, "", "Ford", "GT", 2020, "Red", 1000,
                "http://images.local/ford-gt.jpg", null, "Live", ReservePrice: 20000, null, null),
            TestContext.Current.CancellationToken);

        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have stored a local projection");

        return auctionId;
    }

    [Fact]
    public async Task Publish_AuctionCancelled_MarksTheLocalAuctionFinished()
    {
        var auctionId = await CreateLiveAuctionAsync("carol", DateTime.UtcNow.AddDays(7));
        var bus = factory.Services.GetRequiredService<IBus>();

        await bus.Publish(new AuctionCancelled(auctionId.ToString(), "carol"), TestContext.Current.CancellationToken);

        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished,
            because: "AuctionCancelledConsumer should have marked the local auction finished");
    }

    [Fact]
    public async Task PlaceBid_OnACancelledAuction_ReturnsFinishedStatus()
    {
        var auctionId = await CreateLiveAuctionAsync("carol", DateTime.UtcNow.AddDays(7));
        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(new AuctionCancelled(auctionId.ToString(), "carol"), TestContext.Current.CancellationToken);
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished);

        var client = CreateClient(asUser: "dave", emailVerified: true); // carol is the seller, not dave
        var dto = new PlaceBidDto { AuctionId = auctionId, Amount = 30000 };

        var response = await client.PostAsJsonAsync("api/bids", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bid = await response.Content.ReadFromJsonAsync<BidDto>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bid);
        Assert.Equal("Finished", bid!.BidStatus);
    }

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_NeverFinalizesACancelledAuctionEvenAfterItsEndHasPassed()
    {
        // Already past AuctionEnd at creation time — exactly the case the finalizer exists to
        // pick up, were it not for the cancellation below.
        var auctionId = await CreateLiveAuctionAsync("carol", DateTime.UtcNow.AddMinutes(-10));
        var bus = factory.Services.GetRequiredService<IBus>();

        await using var harness = await RabbitMqPublishHarness<AuctionFinished>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        await bus.Publish(new AuctionCancelled(auctionId.ToString(), "carol"), TestContext.Current.CancellationToken);
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished);

        using (var scope = factory.Services.CreateScope())
        {
            var finalizationService = scope.ServiceProvider.GetRequiredService<IAuctionFinalizationService>();
            await finalizationService.FinalizeExpiredAuctionsAsync(TestContext.Current.CancellationToken);
        }

        var matches = await harness.CountAfterQuietPeriodAsync(
            m => m.AuctionId == auctionId.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(0, matches);
    }
}
