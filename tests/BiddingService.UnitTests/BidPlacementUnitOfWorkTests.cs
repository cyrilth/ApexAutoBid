using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Infrastructure.Data;
using Contracts;
using MassTransit;
using MassTransit.MongoDbIntegration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="BidPlacementUnitOfWork"/> (phase-end code review Critical 1) — the
/// atomic-accept claim, in-place downgrade, and bounded transient-conflict retry. Exercised
/// directly against substituted <see cref="MongoDbContext"/>/<see cref="MongoDbCollectionContext{T}"/>
/// (both public interfaces MassTransit.MongoDbIntegration exposes specifically so this is
/// possible — decompile-confirmed during this task) rather than a real MongoDB, per the
/// phase-end review's own "test the unit-of-work seam directly" allowance — deterministic and
/// fast, with no flake risk from genuine concurrency.
/// </summary>
public class BidPlacementUnitOfWorkTests
{
    private static Bid SampleBid(Guid? auctionId = null, int amount = 25000, BidStatus status = BidStatus.Accepted) => new()
    {
        Id = Guid.NewGuid(),
        AuctionId = auctionId ?? Guid.NewGuid(),
        Bidder = "alice",
        BidderEmail = "alice@apexautobid.local",
        BidTime = new DateTime(2026, 6, 15, 12, 30, 0, DateTimeKind.Utc),
        Amount = amount,
        BidStatus = status
    };

    private static BidPlaced SampleEvent(Bid bid) => new(
        bid.Id.ToString(), bid.AuctionId.ToString(), bid.Bidder, bid.BidTime, bid.Amount, bid.BidStatus.ToString());

    private sealed record Fixture(
        MongoDbContext MongoContext,
        MongoDbCollectionContext<AuctionDocument> AuctionCollection,
        MongoDbCollectionContext<BidDocument> BidCollection,
        IPublishEndpoint PublishEndpoint,
        BidPlacementUnitOfWork Sut);

    private static Fixture BuildFixture()
    {
        var mongoContext = Substitute.For<MongoDbContext>();
        var auctionCollection = Substitute.For<MongoDbCollectionContext<AuctionDocument>>();
        var bidCollection = Substitute.For<MongoDbCollectionContext<BidDocument>>();
        mongoContext.GetCollection<AuctionDocument>().Returns(auctionCollection);
        mongoContext.GetCollection<BidDocument>().Returns(bidCollection);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var sut = new BidPlacementUnitOfWork(mongoContext, publishEndpoint, NullLogger<BidPlacementUnitOfWork>.Instance);

        return new Fixture(mongoContext, auctionCollection, bidCollection, publishEndpoint, sut);
    }

    // ── Claim succeeds — status stays Accepted/AcceptedBelowReserve, event is published ──

    [Fact]
    public async Task SaveAsync_WhenTheAtomicClaimSucceeds_KeepsTheTentativeStatusInsertsAndPublishes()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.Accepted, amount: 25000);
        var bidPlacedEvent = SampleEvent(bid);

        // Non-null return = the filter matched (CurrentHigh < Amount) and CurrentHigh was
        // raised — MongoDbCollectionContext<T>.Lock's own documented contract.
        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = bid.AuctionId, Seller = "bob", CurrentHigh = 25000 });

        await fixture.Sut.SaveAsync(bid, bidPlacedEvent, CancellationToken.None);

        Assert.Equal(BidStatus.Accepted, bid.BidStatus);
        await fixture.BidCollection.Received(1).InsertOne(
            Arg.Is<BidDocument>(d => d.Id == bid.Id && d.BidStatus == "Accepted" && d.Amount == 25000),
            Arg.Any<CancellationToken>());
        await fixture.PublishEndpoint.Received(1).Publish(bidPlacedEvent, Arg.Any<CancellationToken>());
        await fixture.MongoContext.Received(1).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().AbortTransaction(Arg.Any<CancellationToken>());
    }

    // ── Claim fails — downgraded to TooLow in place, no publish ──────────────────────────

    [Fact]
    public async Task SaveAsync_WhenTheAtomicClaimLosesTheRace_DowngradesToTooLowInPlaceAndNeverPublishes()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.AcceptedBelowReserve, amount: 15000);
        var bidPlacedEvent = SampleEvent(bid);

        // Null return = the filter did NOT match — some other bid already raised CurrentHigh to
        // at least this amount.
        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns((AuctionDocument)null!);

        await fixture.Sut.SaveAsync(bid, bidPlacedEvent, CancellationToken.None);

        Assert.Equal(BidStatus.TooLow, bid.BidStatus);
        await fixture.BidCollection.Received(1).InsertOne(
            Arg.Is<BidDocument>(d => d.Id == bid.Id && d.BidStatus == "TooLow"),
            Arg.Any<CancellationToken>());
        await fixture.PublishEndpoint.DidNotReceive().Publish(Arg.Any<BidPlaced>(), Arg.Any<CancellationToken>());
        await fixture.MongoContext.Received(1).CommitTransaction(Arg.Any<CancellationToken>());
    }

    // ── Tentative TooLow/Finished bids never touch the auction document at all ───────────

    [Fact]
    public async Task SaveAsync_WhenTheBidIsAlreadyTooLow_NeverAttemptsTheAtomicClaim()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.TooLow, amount: 5000);

        await fixture.Sut.SaveAsync(bid, bidPlacedEvent: null, CancellationToken.None);

        await fixture.AuctionCollection.DidNotReceive().Lock(
            Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>());
        await fixture.BidCollection.Received(1).InsertOne(
            Arg.Is<BidDocument>(d => d.BidStatus == "TooLow"), Arg.Any<CancellationToken>());
        await fixture.PublishEndpoint.DidNotReceive().Publish(Arg.Any<BidPlaced>(), Arg.Any<CancellationToken>());
    }

    // ── Bounded retry — a transient write conflict at commit time retries the whole attempt ──

    [Fact]
    public async Task SaveAsync_WhenCommitHitsATransientWriteConflictOnce_RetriesTheWholeAttemptAndSucceeds()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.Accepted, amount: 25000);
        var bidPlacedEvent = SampleEvent(bid);

        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = bid.AuctionId, Seller = "bob", CurrentHigh = 25000 });

        var transientEx = new MongoException("write conflict");
        transientEx.AddErrorLabel("TransientTransactionError");

        // First CommitTransaction call fails transiently; the second (retried) attempt succeeds.
        fixture.MongoContext.CommitTransaction(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(transientEx), _ => Task.CompletedTask);

        await fixture.Sut.SaveAsync(bid, bidPlacedEvent, CancellationToken.None);

        Assert.Equal(BidStatus.Accepted, bid.BidStatus);
        await fixture.MongoContext.Received(2).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.Received(1).AbortTransaction(Arg.Any<CancellationToken>());
        // The retried attempt re-does the insert/publish too — this whole attempt is retried,
        // not resumed.
        await fixture.BidCollection.Received(2).InsertOne(Arg.Any<BidDocument>(), Arg.Any<CancellationToken>());
        await fixture.PublishEndpoint.Received(2).Publish(bidPlacedEvent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_WhenEveryAttemptHitsATransientWriteConflict_ExhaustsRetriesAndPropagates()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.Accepted, amount: 25000);
        var bidPlacedEvent = SampleEvent(bid);

        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = bid.AuctionId, Seller = "bob", CurrentHigh = 25000 });

        var transientEx = new MongoException("write conflict");
        transientEx.AddErrorLabel("TransientTransactionError");
        fixture.MongoContext.CommitTransaction(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(transientEx));

        await Assert.ThrowsAsync<MongoException>(
            () => fixture.Sut.SaveAsync(bid, bidPlacedEvent, CancellationToken.None));

        // 3 bounded attempts total (BidPlacementUnitOfWork's own MaxAttempts).
        await fixture.MongoContext.Received(3).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.Received(3).AbortTransaction(Arg.Any<CancellationToken>());
    }

    // ── A non-transient failure is never retried ─────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_WhenAnUnexpectedNonTransientErrorOccurs_PropagatesImmediatelyWithoutRetrying()
    {
        var fixture = BuildFixture();
        var bid = SampleBid(status: BidStatus.Accepted, amount: 25000);
        var bidPlacedEvent = SampleEvent(bid);

        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = bid.AuctionId, Seller = "bob", CurrentHigh = 25000 });

        fixture.PublishEndpoint.Publish(Arg.Any<BidPlaced>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("RabbitMQ unreachable")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Sut.SaveAsync(bid, bidPlacedEvent, CancellationToken.None));

        await fixture.MongoContext.Received(1).AbortTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().CommitTransaction(Arg.Any<CancellationToken>());
        // Only one attempt — not a transient-transaction-error, so no retry.
        await fixture.BidCollection.Received(1).InsertOne(Arg.Any<BidDocument>(), Arg.Any<CancellationToken>());
    }
}
