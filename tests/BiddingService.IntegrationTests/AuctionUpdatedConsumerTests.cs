using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BiddingService.Application.DTOs;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for consuming <c>AuctionUpdated</c> (Phase 11 Task 5.3), exercising the
/// full real broker + real Mongo pipeline: a non-null <c>AuctionEnd</c> is applied to the local
/// auction projection (how an admin's "end now" reaches this service's own background
/// finalizer), while a <c>null</c> <c>AuctionEnd</c> leaves the local record untouched.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class AuctionUpdatedConsumerTests(CustomWebAppFactory factory)
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

    private static AuctionUpdated SampleUpdate(Guid auctionId, DateTime? auctionEnd) => new(
        Id: auctionId.ToString(),
        Make: "Ford",
        Model: "GT",
        Color: "Red",
        Mileage: 1000,
        Year: 2020,
        ImageUrl: "http://images.local/ford-gt.jpg",
        ThumbnailUrl: null,
        AuctionEnd: auctionEnd);

    [Fact]
    public async Task Publish_AuctionUpdated_WithAuctionEnd_UpdatesTheLocalAuctionEnd()
    {
        var auctionId = await CreateLiveAuctionAsync("carol", DateTime.UtcNow.AddDays(7));
        var newEnd = DateTime.UtcNow.AddMinutes(-1); // admin "end now"
        var bus = factory.Services.GetRequiredService<IBus>();

        await bus.Publish(SampleUpdate(auctionId, newEnd), TestContext.Current.CancellationToken);

        var auction = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken,
            a => a.AuctionEnd <= DateTime.UtcNow,
            because: "AuctionUpdatedConsumer should have applied the new AuctionEnd");
        Assert.True(Math.Abs((auction.AuctionEnd - newEnd).TotalSeconds) < 2);
    }

    [Fact]
    public async Task PlaceBid_AfterAnAdminEndNowUpdate_ReturnsFinishedStatus()
    {
        var auctionId = await CreateLiveAuctionAsync("carol", DateTime.UtcNow.AddDays(7));
        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(
            SampleUpdate(auctionId, DateTime.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken,
            a => a.AuctionEnd <= DateTime.UtcNow);

        var client = CreateClient(asUser: "dave", emailVerified: true);
        var dto = new PlaceBidDto { AuctionId = auctionId, Amount = 30000 };

        var response = await client.PostAsJsonAsync("api/bids", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bid = await response.Content.ReadFromJsonAsync<BidDto>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bid);
        Assert.Equal("Finished", bid!.BidStatus);
    }

    [Fact]
    public async Task Publish_AuctionUpdated_WithoutAuctionEnd_LeavesTheLocalAuctionEndUnchanged()
    {
        var originalEnd = DateTime.UtcNow.AddDays(7);
        var auctionId = await CreateLiveAuctionAsync("carol", originalEnd);
        var bus = factory.Services.GetRequiredService<IBus>();

        await bus.Publish(SampleUpdate(auctionId, auctionEnd: null), TestContext.Current.CancellationToken);

        // No positive signal exists for "this update was correctly ignored" — settle for
        // comfortably longer than this broker/consumer pair ever takes to process a message in
        // this suite, then assert AuctionEnd is still exactly what it was at creation.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<BiddingService.Domain.Interfaces.IAuctionRepository>();
        var auction = await repository.GetByIdAsync(auctionId, TestContext.Current.CancellationToken);

        Assert.NotNull(auction);
        Assert.Equal(originalEnd, auction!.AuctionEnd, TimeSpan.FromSeconds(1));
    }
}
