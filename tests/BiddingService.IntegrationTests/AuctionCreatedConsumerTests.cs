using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for <c>AuctionCreatedConsumer</c> (Phase 5 Task 16.3), exercising the full
/// real broker + real Mongo inbox/outbox pipeline. Mirrors
/// <c>SearchService.IntegrationTests.AuctionCreatedConsumerTests</c>'s identical shape and
/// rationale, EXCEPT for redelivery semantics: unlike SearchService's own consumer (a genuine
/// whole-document upsert is correct there), this service's <c>AuctionCreatedConsumer</c> is
/// deliberately insert-only-if-absent (phase-end code review Warning 3) — see
/// <c>IAuctionRepository.InsertIfNotExistsAsync</c>'s own remarks for why.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class AuctionCreatedConsumerTests(CustomWebAppFactory factory)
{
    private async Task<Auction> GetAuctionAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var auction = await scope.ServiceProvider.GetRequiredService<IAuctionRepository>()
            .GetByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(auction);
        return auction!;
    }

    [Fact]
    public async Task Publish_AuctionCreated_StoresLocalAuctionProjectionWithMappedFields()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var auctionEnd = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        var bus = factory.Services.GetRequiredService<IBus>();

        await bus.Publish(
            new AuctionCreated(
                id, createdAt, createdAt, auctionEnd, "carol", "", "Ford", "GT", 2020, "Red", 12345,
                "http://images.local/ford-gt.jpg", "http://images.local/ford-gt-thumb.jpg", "Live",
                20000, null, null),
            TestContext.Current.CancellationToken);

        var auction = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, id, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have stored a local projection");

        Assert.Equal(id, auction.Id);
        Assert.Equal("carol", auction.Seller);
        Assert.Equal(20000, auction.ReservePrice);
        Assert.Equal(auctionEnd, auction.AuctionEnd);
        // A freshly-consumed AuctionCreated is never already finished (BidMappingConfig's
        // AuctionCreated -> Auction rule leaves Finished at its default).
        Assert.False(auction.Finished);
    }

    [Fact]
    public async Task Publish_AuctionCreated_Twice_WithSameId_IsIdempotentNoOpOnRedelivery()
    {
        // Redelivery/reprocessing of the same auction id must be a genuine no-op — NOT a
        // whole-document overwrite (phase-end code review Warning 3): AuctionCreated describes
        // the auction's CREATION, so a redelivered/duplicate copy of it must never change
        // anything about an already-stored record.
        var id = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();

        Task PublishAsync(int reservePrice) => bus.Publish(
            new AuctionCreated(
                id, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                "carol", "", "Ford", "GT", 2020, "Red", 1000,
                "http://images.local/ford-gt.jpg", null, "Live", reservePrice, null, null),
            TestContext.Current.CancellationToken);

        await PublishAsync(20000);
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, id, TestContext.Current.CancellationToken, a => a.ReservePrice == 20000);

        await PublishAsync(30000);

        // No positive signal exists for "this redelivery was correctly ignored" — settle for
        // comfortably longer than this broker/consumer pair ever takes to process a message in
        // this suite (RepositoryPolling's own 20s ceiling elsewhere), then assert the record is
        // still exactly what the FIRST publish left it as.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var auction = await GetAuctionAsync(id);

        Assert.Equal(20000, auction.ReservePrice);
    }

    // ── Warning 3 — a replay after finalization must not resurrect the auction ──────────

    [Fact]
    public async Task Publish_AuctionCreated_AfterTheAuctionHasAlreadyBeenFinalized_DoesNotResurrectOrChangeIt()
    {
        var id = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();

        // 1. Create it, already past AuctionEnd, via the normal AuctionCreated path.
        await bus.Publish(
            new AuctionCreated(
                id, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-10),
                "carol", "", "Ford", "GT", 2020, "Red", 12345,
                "http://images.local/ford-gt.jpg", null, "Live", 20000, null, null),
            TestContext.Current.CancellationToken);

        var stored = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, id, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have stored a local projection");
        Assert.False(stored.Finished);

        // 2. Finalize it for real (no bids -> unsold), through the same Application-layer path
        // the background finalizer uses — proves the SAME record a replay must not disturb.
        using (var scope = factory.Services.CreateScope())
        {
            var finalizationService = scope.ServiceProvider.GetRequiredService<IAuctionFinalizationService>();
            await finalizationService.FinalizeExpiredAuctionsAsync(TestContext.Current.CancellationToken);
        }
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, id, TestContext.Current.CancellationToken, a => a.Finished,
            because: "the auction should now be finalized");

        // 3. Replay the SAME AuctionCreated event — deliberately with a DIFFERENT reserve
        // price, so a whole-document-replace regression would be unmistakable (both a
        // resurrected Finished=false AND a changed ReservePrice).
        await bus.Publish(
            new AuctionCreated(
                id, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-10),
                "carol", "", "Ford", "GT", 2020, "Red", 12345,
                "http://images.local/ford-gt.jpg", null, "Live", 99999, null, null),
            TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var afterReplay = await GetAuctionAsync(id);

        Assert.True(afterReplay.Finished);
        Assert.Equal(20000, afterReplay.ReservePrice);
    }
}
