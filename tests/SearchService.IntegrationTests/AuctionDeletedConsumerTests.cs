using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.IntegrationTests;

/// <summary>
/// Integration tests for <c>AuctionDeletedConsumer</c> (Phase 2 Task 10.3).
/// </summary>
[Collection(SearchServiceApiCollection.Name)]
public class AuctionDeletedConsumerTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Publish_AuctionDeleted_RemovesTheItem()
    {
        var id = Guid.NewGuid();

        var item = new Item
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AuctionEnd = DateTime.UtcNow.AddDays(7),
            Seller = "bob",
            Make = "Ford",
            Model = "GT",
            Year = 2020,
            Color = "Red",
            Mileage = 1000,
            ImageUrl = "http://images.local/ford-gt.jpg",
            Status = "Live",
            ReservePrice = 20000
        };
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IItemRepository>()
                .UpsertAsync(item, TestContext.Current.CancellationToken);
        }

        // Sanity: confirm it's actually there before deleting, so a false-positive "absent"
        // (e.g. a typo'd id) can't slip past WaitForItemAbsentAsync unnoticed.
        var seeded = await MongoPolling.WaitForItemAsync(
            factory.Services, id, TestContext.Current.CancellationToken,
            because: "test setup should have seeded it");
        Assert.Equal(id, seeded.Id);

        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(new AuctionDeleted(id.ToString()), TestContext.Current.CancellationToken);

        await MongoPolling.WaitForItemAbsentAsync(factory.Services, id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Publish_AuctionDeleted_ForIdThatWasNeverIndexed_SucceedsSilently()
    {
        // Idempotency guard (Task 4): deleting an id that doesn't exist must not error or
        // otherwise disrupt the consumer/bus.
        var id = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();

        await bus.Publish(new AuctionDeleted(id.ToString()), TestContext.Current.CancellationToken);

        await MongoPolling.WaitForItemAbsentAsync(factory.Services, id, TestContext.Current.CancellationToken);
    }
}
