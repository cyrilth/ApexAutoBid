using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.IntegrationTests;

/// <summary>
/// Integration tests for <c>AuctionUpdatedConsumer</c> (Phase 2 Task 10.2).
/// </summary>
[Collection(SearchServiceApiCollection.Name)]
public class AuctionUpdatedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionUpdated_UpdatesChangedFieldsAndLeavesOthersIntact()
    {
        var id = Guid.NewGuid();

        // Seed directly via the repository (an allowed alternative to seeding through a
        // published AuctionCreated — Task 10.2) so this test focuses purely on
        // AuctionUpdatedConsumer's behavior.
        var original = new Item
        {
            Id = id,
            CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            AuctionEnd = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            Seller = "bob",
            Winner = null,
            Make = "Ford",
            Model = "GT",
            Year = 2020,
            Color = "Red",
            Mileage = 1000,
            ImageUrl = "http://images.local/ford-gt.jpg",
            ThumbnailUrl = null,
            Status = "Live",
            ReservePrice = 20000,
            SoldAmount = null,
            CurrentHighBid = null
        };
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IItemRepository>()
                .UpsertAsync(original, TestContext.Current.CancellationToken);
        }

        var newAuctionEnd = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var bus = factory.Services.GetRequiredService<IBus>();

        // AuctionUpdated only carries Item-level fields + AuctionEnd — Seller/Winner/Status/
        // ReservePrice/SoldAmount/CurrentHighBid are NOT part of this contract, so they must
        // remain exactly as seeded.
        await bus.Publish(
            new AuctionUpdated(
                id.ToString(),
                "Chevrolet",
                "Corvette",
                "Blue",
                2000,
                2021,
                "http://images.local/corvette.jpg",
                "http://images.local/corvette-thumb.jpg",
                newAuctionEnd),
            TestContext.Current.CancellationToken);

        var updated = await MongoPolling.WaitForItemAsync(
            factory.Services, id, TestContext.Current.CancellationToken,
            item => item.Make == "Chevrolet",
            because: "AuctionUpdatedConsumer should have applied the changed fields");

        // Changed fields
        Assert.Equal("Chevrolet", updated.Make);
        Assert.Equal("Corvette", updated.Model);
        Assert.Equal("Blue", updated.Color);
        Assert.Equal(2000, updated.Mileage);
        Assert.Equal(2021, updated.Year);
        Assert.Equal("http://images.local/corvette.jpg", updated.ImageUrl);
        Assert.Equal("http://images.local/corvette-thumb.jpg", updated.ThumbnailUrl);
        Assert.Equal(newAuctionEnd, updated.AuctionEnd);

        // Untouched fields — must survive the partial update exactly as seeded.
        Assert.Equal("bob", updated.Seller);
        Assert.Null(updated.Winner);
        Assert.Equal("Live", updated.Status);
        Assert.Equal(20000, updated.ReservePrice);
        Assert.Null(updated.SoldAmount);
        Assert.Null(updated.CurrentHighBid);
    }
}
