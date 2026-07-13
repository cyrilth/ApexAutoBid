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
/// Unit tests for <see cref="BidRemovalUnitOfWork"/> (Phase 11 Task 5.1/5.5 / Task 9.2) — the
/// current-high-bid recalculation, the <c>BidRemoved</c> publish, and the audit-entry insert.
/// Exercised directly against substituted <see cref="MongoDbContext"/>/
/// <see cref="MongoDbCollectionContext{T}"/>/<see cref="IFindFluent{TDocument,TProjection}"/>/
/// <see cref="IAsyncCursor{TDocument}"/> — all genuine public interfaces MassTransit.MongoDbIntegration
/// and MongoDB.Driver expose — rather than a real MongoDB, mirroring
/// <c>BidPlacementUnitOfWorkTests</c>'/<c>AuctionFinalizationUnitOfWorkTests</c>' identical "test
/// the unit-of-work seam directly" convention.
/// </summary>
public class BidRemovalUnitOfWorkTests
{
    private static Bid SampleBid(Guid? id = null, Guid? auctionId = null, int amount = 18000) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AuctionId = auctionId ?? Guid.NewGuid(),
        Bidder = "alice",
        BidderEmail = "alice@apexautobid.local",
        BidTime = new DateTime(2026, 6, 15, 11, 0, 0, DateTimeKind.Utc),
        Amount = amount,
        BidStatus = BidStatus.Accepted
    };

    private static AuditEntry SampleAuditEntry(Bid bid) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = new DateTime(2026, 6, 15, 12, 30, 0, DateTimeKind.Utc),
        Actor = "admin",
        ActorIsAdmin = true,
        Action = "BidRemoved",
        EntityType = "Bid",
        EntityId = bid.Id.ToString(),
        Data = "{}"
    };

    private sealed record Fixture(
        MongoDbContext MongoContext,
        MongoDbCollectionContext<BidDocument> BidCollection,
        MongoDbCollectionContext<AuctionDocument> AuctionCollection,
        MongoDbCollectionContext<AuditEntryDocument> AuditCollection,
        IPublishEndpoint PublishEndpoint,
        BidRemovalUnitOfWork Sut);

    private static Fixture BuildFixture(IReadOnlyList<BidDocument> remainingBids)
    {
        var mongoContext = Substitute.For<MongoDbContext>();
        var bidCollection = Substitute.For<MongoDbCollectionContext<BidDocument>>();
        var auctionCollection = Substitute.For<MongoDbCollectionContext<AuctionDocument>>();
        var auditCollection = Substitute.For<MongoDbCollectionContext<AuditEntryDocument>>();
        mongoContext.GetCollection<BidDocument>().Returns(bidCollection);
        mongoContext.GetCollection<AuctionDocument>().Returns(auctionCollection);
        mongoContext.GetCollection<AuditEntryDocument>().Returns(auditCollection);

        // The session-enlisted Find(...).ToCursorAsync() chain, substituted all the way down to
        // a single-page IAsyncCursor that yields `remainingBids` once, then signals end-of-cursor.
        var findFluent = Substitute.For<IFindFluent<BidDocument, BidDocument>>();
        var cursor = Substitute.For<IAsyncCursor<BidDocument>>();
        cursor.Current.Returns(remainingBids);
        cursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(true, false);
        findFluent.ToCursorAsync(Arg.Any<CancellationToken>()).Returns(cursor);
        bidCollection.Find(Arg.Any<FilterDefinition<BidDocument>>()).Returns(findFluent);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var sut = new BidRemovalUnitOfWork(mongoContext, publishEndpoint, NullLogger<BidRemovalUnitOfWork>.Instance);

        return new Fixture(mongoContext, bidCollection, auctionCollection, auditCollection, publishEndpoint, sut);
    }

    // ── Recalculation — the highest remaining accepted bid becomes the new current high ────

    [Fact]
    public async Task RemoveAsync_WhenAcceptedBidsRemain_RecalculatesTheHighestAsCurrentHighAndPublishesBidRemoved()
    {
        var bid = SampleBid(amount: 25000);
        var auditEntry = SampleAuditEntry(bid);
        var remaining = new List<BidDocument>
        {
            new()
            {
                Id = Guid.NewGuid(), AuctionId = bid.AuctionId, Bidder = "tom",
                BidderEmail = "tom@apexautobid.local", Amount = 18000,
                BidStatus = nameof(BidStatus.AcceptedBelowReserve), BidTime = bid.BidTime.AddMinutes(-10)
            },
            new()
            {
                Id = Guid.NewGuid(), AuctionId = bid.AuctionId, Bidder = "bob",
                BidderEmail = "bob@apexautobid.local", Amount = 20000,
                BidStatus = nameof(BidStatus.Accepted), BidTime = bid.BidTime.AddMinutes(-5)
            },
        };
        var fixture = BuildFixture(remaining);

        var result = await fixture.Sut.RemoveAsync(bid, auditEntry, CancellationToken.None);

        Assert.Equal(20000, result);

        await fixture.BidCollection.Received(1).DeleteOne(
            Arg.Any<FilterDefinition<BidDocument>>(), Arg.Any<CancellationToken>());

        await fixture.AuctionCollection.Received(1).Lock(
            Arg.Any<FilterDefinition<AuctionDocument>>(),
            Arg.Any<UpdateDefinition<AuctionDocument>>(),
            Arg.Any<CancellationToken>());

        await fixture.PublishEndpoint.Received(1).Publish(
            Arg.Is<BidRemoved>(e =>
                e.BidId == bid.Id.ToString() &&
                e.AuctionId == bid.AuctionId.ToString() &&
                e.CurrentHighBid == 20000),
            Arg.Any<CancellationToken>());

        await fixture.AuditCollection.Received(1).InsertOne(
            Arg.Is<AuditEntryDocument>(d => d.Id == auditEntry.Id && d.Action == "BidRemoved"),
            Arg.Any<CancellationToken>());

        await fixture.MongoContext.Received(1).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().AbortTransaction(Arg.Any<CancellationToken>());
    }

    // ── No remaining accepted bids — CurrentHighBid is null, AuctionDocument.CurrentHigh is 0 ──

    [Fact]
    public async Task RemoveAsync_WhenNoAcceptedBidsRemain_PublishesNullCurrentHighBid()
    {
        var bid = SampleBid();
        var auditEntry = SampleAuditEntry(bid);
        var fixture = BuildFixture([]);

        var result = await fixture.Sut.RemoveAsync(bid, auditEntry, CancellationToken.None);

        Assert.Null(result);

        await fixture.PublishEndpoint.Received(1).Publish(
            Arg.Is<BidRemoved>(e => e.CurrentHighBid == null), Arg.Any<CancellationToken>());

        // AuctionDocument.CurrentHigh (a plain, non-nullable int — see its own remarks) is set
        // to 0 rather than left untouched when no accepted bid remains, so the next PlaceBid's
        // atomic claim starts from a clean slate exactly like a brand-new auction would.
        await fixture.AuctionCollection.Received(1).Lock(
            Arg.Any<FilterDefinition<AuctionDocument>>(),
            Arg.Any<UpdateDefinition<AuctionDocument>>(),
            Arg.Any<CancellationToken>());
    }

    // ── Deterministic tiebreak — equal amounts resolve by BidTime asc, then Id asc ──────────

    [Fact]
    public async Task RemoveAsync_WhenTwoRemainingBidsShareTheHighestAmount_BreaksTheTieByEarliestBidTime()
    {
        var bid = SampleBid();
        var auditEntry = SampleAuditEntry(bid);
        var earlier = new BidDocument
        {
            Id = Guid.NewGuid(), AuctionId = bid.AuctionId, Bidder = "tom",
            BidderEmail = "tom@apexautobid.local", Amount = 20000,
            BidStatus = nameof(BidStatus.Accepted), BidTime = bid.BidTime.AddMinutes(-10)
        };
        var later = new BidDocument
        {
            Id = Guid.NewGuid(), AuctionId = bid.AuctionId, Bidder = "bob",
            BidderEmail = "bob@apexautobid.local", Amount = 20000,
            BidStatus = nameof(BidStatus.Accepted), BidTime = bid.BidTime.AddMinutes(-5)
        };
        // Order in the "cursor" deliberately doesn't match tiebreak order — the sut must sort,
        // not trust arrival order.
        var fixture = BuildFixture([later, earlier]);

        var result = await fixture.Sut.RemoveAsync(bid, auditEntry, CancellationToken.None);

        Assert.Equal(20000, result);
    }

    // ── A failure aborts the transaction and propagates, never publishing ───────────────────

    [Fact]
    public async Task RemoveAsync_WhenPublishFails_AbortsTheTransactionAndPropagates()
    {
        var bid = SampleBid();
        var auditEntry = SampleAuditEntry(bid);
        var fixture = BuildFixture([]);
        fixture.PublishEndpoint.Publish(Arg.Any<BidRemoved>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("RabbitMQ unreachable")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Sut.RemoveAsync(bid, auditEntry, CancellationToken.None));

        await fixture.MongoContext.Received(1).AbortTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.AuditCollection.DidNotReceive().InsertOne(
            Arg.Any<AuditEntryDocument>(), Arg.Any<CancellationToken>());
    }
}
