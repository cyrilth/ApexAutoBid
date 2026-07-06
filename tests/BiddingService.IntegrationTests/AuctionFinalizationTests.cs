using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using BiddingService.Infrastructure.Data;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for the background auction finalizer's winner semantics and idempotency
/// (Phase 5 Task 16.4), exercising the real Mongo bus-outbox + RabbitMQ pipeline
/// (<c>AuctionFinalizationUnitOfWork</c>) exactly as <c>AuctionFinalizerHostedService</c> would
/// drive it.
/// </summary>
/// <remarks>
/// <b>Why <see cref="IAuctionFinalizationService"/> is invoked directly rather than waiting on
/// the real hosted-service timer:</b> the shared <see cref="BiddingServiceApiCollection"/>
/// factory deliberately runs with a one-hour finalization interval (so it never fires
/// unexpectedly during any OTHER test in this collection) — waiting on that same timer here
/// would mean either shortening it (reintroducing the exact cross-test race this suite's design
/// explicitly avoids — see <c>CustomWebAppFactory</c>'s remarks) or a genuinely flaky wait. The
/// business logic under test — winner selection, event shape, the finalized-flag write — lives
/// entirely in <see cref="AuctionFinalizationAppService"/> (Application layer); the hosted
/// service (<c>AuctionFinalizerHostedService</c>) is deliberately "dumb", owning only the timer
/// and per-tick DI scope (see its own remarks). Resolving
/// <see cref="IAuctionFinalizationService"/> from a fresh scope and calling it directly
/// exercises that exact same Application-layer logic, the same Infrastructure unit-of-work, and
/// the same real Mongo/RabbitMQ containers — the only thing not exercised is the
/// <see cref="PeriodicTimer"/> loop itself, which
/// <c>AuctionFinalizerHostedServiceTests</c>' single coarse smoke test covers separately, in its
/// own dedicated short-interval collection.
/// </remarks>
[Collection(BiddingServiceApiCollection.Name)]
public class AuctionFinalizationTests(CustomWebAppFactory factory)
{
    private static Auction ExpiredAuction(Guid id, string seller, int reservePrice) => new()
    {
        Id = id,
        Seller = seller,
        ReservePrice = reservePrice,
        AuctionEnd = DateTime.UtcNow.AddMinutes(-5),
        Finished = false
    };

    private async Task SeedAuctionAsync(Auction auction)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuctionRepository>()
            .InsertIfNotExistsAsync(auction, TestContext.Current.CancellationToken);
    }

    // IBidRepository is read-only by design (BidPlacementUnitOfWork owns the only write path —
    // see its own remarks), so seeding a bid directly for this test goes straight through
    // MongoDB.Entities against BidDocument, exactly like DbInitializer.SeedDataAsync itself
    // does — an allowed alternative to seeding through a published event, mirroring
    // SearchService.IntegrationTests.AuctionUpdatedConsumerTests' identical "seed directly via
    // the repository" convention, one layer lower since no repository write method exists here.
    private async Task SeedBidAsync(
        Guid auctionId, string bidder, string bidderEmail, int amount, BidStatus status, DateTime bidTime)
    {
        using var scope = factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoDbConnection>();
        await mongo.Instance.SaveAsync(
            new BidDocument
            {
                Id = Guid.NewGuid(),
                AuctionId = auctionId,
                Bidder = bidder,
                BidderEmail = bidderEmail,
                Amount = amount,
                BidStatus = status.ToString(),
                BidTime = bidTime
            },
            TestContext.Current.CancellationToken);
    }

    private async Task FinalizeNowAsync()
    {
        using var scope = factory.Services.CreateScope();
        var finalizationService = scope.ServiceProvider.GetRequiredService<IAuctionFinalizationService>();
        await finalizationService.FinalizeExpiredAuctionsAsync(TestContext.Current.CancellationToken);
    }

    // ── Winner semantics: highest strictly-Accepted bid wins ─────────────────────

    [Fact]
    public async Task FinalizeExpiredAuctions_WhenHighBidIsStrictlyAccepted_PublishesAuctionFinishedSoldWithWinner()
    {
        var auctionId = Guid.NewGuid();
        await SeedAuctionAsync(ExpiredAuction(auctionId, seller: "bob", reservePrice: 20000));
        await SeedBidAsync(
            auctionId, "alice", "alice@apexautobid.local", 25000,
            BidStatus.Accepted, DateTime.UtcNow.AddMinutes(-10));

        await using var harness = await RabbitMqPublishHarness<AuctionFinished>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        await FinalizeNowAsync();

        var published = await harness.WaitForMessageAsync(
            m => m.AuctionId == auctionId.ToString(), TestContext.Current.CancellationToken);
        Assert.True(published.ItemSold);
        Assert.Equal("alice", published.Winner);
        Assert.Equal("alice@apexautobid.local", published.WinnerEmail);
        Assert.Equal("bob", published.Seller);
        Assert.Equal(25000, published.Amount);

        var finalized = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished);
        Assert.True(finalized.Finished);

        // Idempotency: a second tick must not re-select this now-Finished auction, so no
        // second AuctionFinished is ever published for it.
        await FinalizeNowAsync();
        var count = await harness.CountAfterQuietPeriodAsync(
            m => m.AuctionId == auctionId.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    // ── Winner semantics: AcceptedBelowReserve-only high bid → not sold, no WinnerEmail ──

    [Fact]
    public async Task FinalizeExpiredAuctions_WhenHighBidIsOnlyAcceptedBelowReserve_PublishesAuctionFinishedNotSoldWithNoWinnerEmail()
    {
        var auctionId = Guid.NewGuid();
        await SeedAuctionAsync(ExpiredAuction(auctionId, seller: "tom", reservePrice: 50000));
        await SeedBidAsync(
            auctionId, "bob", "bob@apexautobid.local", 40000,
            BidStatus.AcceptedBelowReserve, DateTime.UtcNow.AddMinutes(-10));

        await using var harness = await RabbitMqPublishHarness<AuctionFinished>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        await FinalizeNowAsync();

        var published = await harness.WaitForMessageAsync(
            m => m.AuctionId == auctionId.ToString(), TestContext.Current.CancellationToken);
        Assert.False(published.ItemSold);
        Assert.Null(published.Winner);
        Assert.Null(published.WinnerEmail);
        Assert.Null(published.Amount);
        Assert.Equal("tom", published.Seller);

        var finalized = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished);
        Assert.True(finalized.Finished);
    }

    [Fact]
    public async Task FinalizeExpiredAuctions_WhenNoBidsWereEverPlaced_PublishesAuctionFinishedNotSold()
    {
        var auctionId = Guid.NewGuid();
        await SeedAuctionAsync(ExpiredAuction(auctionId, seller: "alice", reservePrice: 10000));

        await using var harness = await RabbitMqPublishHarness<AuctionFinished>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        await FinalizeNowAsync();

        var published = await harness.WaitForMessageAsync(
            m => m.AuctionId == auctionId.ToString(), TestContext.Current.CancellationToken);
        Assert.False(published.ItemSold);
        Assert.Null(published.Winner);
        Assert.Null(published.WinnerEmail);
    }
}

/// <summary>
/// One coarse smoke test proving the REAL <c>AuctionFinalizerHostedService</c> — its own
/// <see cref="PeriodicTimer"/>, its own per-tick DI scope, Program.cs's actual wiring — picks up
/// an expired auction on its own, with nothing in the test invoking
/// <see cref="IAuctionFinalizationService"/> directly. Deliberately the ONLY test against
/// <see cref="ShortIntervalWebAppFactory"/>'s dedicated 2-second-interval containers/collection;
/// every winner-semantics/idempotency assertion belongs to <see cref="AuctionFinalizationTests"/>
/// instead (direct invocation, against the shared long-interval factory) — see this test's own
/// remarks and <c>AuctionFinalizationTests</c>' class-level remarks for why.
/// </summary>
[Collection(BiddingServiceFinalizerCollection.Name)]
public class AuctionFinalizerHostedServiceTests(ShortIntervalWebAppFactory factory)
{
    [Fact]
    public async Task BackgroundFinalizer_TicksOnItsOwnAndFinalizesAnExpiredAuctionWithoutManualInvocation()
    {
        var auctionId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuctionRepository>().InsertIfNotExistsAsync(
                new Auction
                {
                    Id = auctionId, Seller = "bob", ReservePrice = 20000,
                    AuctionEnd = DateTime.UtcNow.AddSeconds(-5), Finished = false
                },
                TestContext.Current.CancellationToken);
        }

        // No seed bid — an unsold outcome is the simplest sufficient proof the hosted service
        // itself is wired correctly and ticking; the winner-selection matrix is already fully
        // covered (unit tests + AuctionFinalizationTests' direct-invocation integration tests).
        var finalized = await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken, a => a.Finished,
            because: "the real background finalizer (its own timer, no direct invocation) should have finalized it");

        Assert.True(finalized.Finished);
    }
}

/// <summary>
/// Integration tests for the finalization grace period (phase-end code review Critical 2),
/// against <see cref="GracePeriodWebAppFactory"/>'s dedicated 30-second-grace containers/
/// collection — deliberately separate from <see cref="BiddingServiceApiCollection"/>'s own
/// zero-grace configuration (see that class's remarks) so this suite's grace-period assertions
/// can never be affected by, or affect, every other test's "immediately eligible past
/// AuctionEnd" assumption.
/// </summary>
[Collection(BiddingServiceGracePeriodCollection.Name)]
public class AuctionFinalizationGracePeriodTests(GracePeriodWebAppFactory factory)
{
    private async Task SeedAuctionAsync(Auction auction)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuctionRepository>()
            .InsertIfNotExistsAsync(auction, TestContext.Current.CancellationToken);
    }

    private async Task<Auction> GetAuctionAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var auction = await scope.ServiceProvider.GetRequiredService<IAuctionRepository>()
            .GetByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(auction);
        return auction!;
    }

    private async Task FinalizeNowAsync()
    {
        using var scope = factory.Services.CreateScope();
        var finalizationService = scope.ServiceProvider.GetRequiredService<IAuctionFinalizationService>();
        await finalizationService.FinalizeExpiredAuctionsAsync(TestContext.Current.CancellationToken);
    }

    // ── Past AuctionEnd but still within the 30s grace — must NOT be finalized ───────────

    [Fact]
    public async Task FinalizeExpiredAuctions_WhenPastAuctionEndButWithinTheGracePeriod_DoesNotFinalizeIt()
    {
        var auctionId = Guid.NewGuid();
        // 5 seconds past AuctionEnd, comfortably inside this factory's 30-second grace.
        await SeedAuctionAsync(new Auction
        {
            Id = auctionId, Seller = "bob", ReservePrice = 20000,
            AuctionEnd = DateTime.UtcNow.AddSeconds(-5), Finished = false
        });

        await FinalizeNowAsync();

        var auction = await GetAuctionAsync(auctionId);
        Assert.False(auction.Finished);
    }

    // ── Past AuctionEnd AND past the grace period — must be finalized ────────────────────

    [Fact]
    public async Task FinalizeExpiredAuctions_WhenPastAuctionEndAndPastTheGracePeriod_FinalizesIt()
    {
        var auctionId = Guid.NewGuid();
        // 60 seconds past AuctionEnd — well beyond this factory's 30-second grace.
        await SeedAuctionAsync(new Auction
        {
            Id = auctionId, Seller = "bob", ReservePrice = 20000,
            AuctionEnd = DateTime.UtcNow.AddSeconds(-60), Finished = false
        });

        await FinalizeNowAsync();

        var auction = await GetAuctionAsync(auctionId);
        Assert.True(auction.Finished);
    }
}
