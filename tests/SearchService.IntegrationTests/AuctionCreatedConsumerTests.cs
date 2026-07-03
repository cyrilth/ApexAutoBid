using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace SearchService.IntegrationTests;

/// <summary>
/// Integration tests for <c>AuctionCreatedConsumer</c> (Phase 2 Task 10.1), exercising the
/// full real broker + real Mongo inbox/outbox transaction pipeline (Phase 2 Tasks 4 + 7).
/// </summary>
[Collection(SearchServiceApiCollection.Name)]
public class AuctionCreatedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionCreated_InsertsItemWithAllFieldsMappedCorrectly()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var auctionEnd = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        var bus = factory.Services.GetRequiredService<IBus>();

        // Full payload, including Winner = "" — exactly what AuctionService actually publishes
        // for a brand-new auction (see AuctionMappingConfig's AuctionDto -> AuctionCreated rule).
        await bus.Publish(
            new AuctionCreated(
                id,
                createdAt,
                updatedAt,
                auctionEnd,
                "bob",
                "",
                "Ford",
                "GT",
                2020,
                "Red",
                12345,
                "http://images.local/ford-gt.jpg",
                "http://images.local/ford-gt-thumb.jpg",
                "Live",
                20000,
                null,
                null),
            TestContext.Current.CancellationToken);

        var item = await MongoPolling.WaitForItemAsync(
            factory.Services, id, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have inserted it");

        Assert.Equal(id, item.Id);
        Assert.Equal(createdAt, item.CreatedAt);
        Assert.Equal(updatedAt, item.UpdatedAt);
        Assert.Equal(auctionEnd, item.AuctionEnd);
        Assert.Equal("bob", item.Seller);
        Assert.Null(item.Winner); // "" normalized to null (Task 3 code review carry-forward)
        Assert.Equal("Ford", item.Make);
        Assert.Equal("GT", item.Model);
        Assert.Equal(2020, item.Year);
        Assert.Equal("Red", item.Color);
        Assert.Equal(12345, item.Mileage);
        Assert.Equal("http://images.local/ford-gt.jpg", item.ImageUrl);
        Assert.Equal("http://images.local/ford-gt-thumb.jpg", item.ThumbnailUrl);
        Assert.Equal("Live", item.Status);
        Assert.Equal(20000, item.ReservePrice);
        Assert.Null(item.SoldAmount);
        Assert.Null(item.CurrentHighBid);
    }

    [Fact]
    public async Task Publish_AuctionCreated_Twice_WithSameId_IsIdempotentUpsert()
    {
        // Redelivery/reprocessing of the same auction id must overwrite, not duplicate —
        // UpsertAsync is keyed on the Guid _id (Task 4).
        var id = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();

        Task PublishAsync(string color) => bus.Publish(
            new AuctionCreated(
                id, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                "bob", "", "Ford", "GT", 2020, color, 1000,
                "http://images.local/ford-gt.jpg", null, "Live", 20000, null, null),
            TestContext.Current.CancellationToken);

        await PublishAsync("Red");
        await MongoPolling.WaitForItemAsync(
            factory.Services, id, TestContext.Current.CancellationToken, item => item.Color == "Red");

        await PublishAsync("Blue");
        var item = await MongoPolling.WaitForItemAsync(
            factory.Services, id, TestContext.Current.CancellationToken, item => item.Color == "Blue");

        Assert.Equal("Blue", item.Color);
    }
}
