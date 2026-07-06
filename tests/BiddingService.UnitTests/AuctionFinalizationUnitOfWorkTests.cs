using BiddingService.Domain.Entities;
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
/// Unit tests for <see cref="AuctionFinalizationUnitOfWork"/> (phase-end code review Critical
/// 2) — the atomic, conditional finalize that makes double-finalization structurally
/// impossible. Exercised directly against substituted <see cref="MongoDbContext"/>/
/// <see cref="MongoDbCollectionContext{T}"/>, mirroring <c>BidPlacementUnitOfWorkTests</c>'
/// identical rationale for testing this unit-of-work seam directly.
/// </summary>
public class AuctionFinalizationUnitOfWorkTests
{
    private static Auction SampleAuction(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Seller = "bob",
        ReservePrice = 20000,
        AuctionEnd = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
        Finished = true // caller always sets this before invoking FinalizeAsync
    };

    private static AuctionFinished SampleEvent(Auction auction) => new(
        ItemSold: false, AuctionId: auction.Id.ToString(), Winner: null, WinnerEmail: null,
        Seller: auction.Seller, Amount: null);

    private sealed record Fixture(
        MongoDbContext MongoContext,
        MongoDbCollectionContext<AuctionDocument> AuctionCollection,
        IPublishEndpoint PublishEndpoint,
        AuctionFinalizationUnitOfWork Sut);

    private static Fixture BuildFixture()
    {
        var mongoContext = Substitute.For<MongoDbContext>();
        var auctionCollection = Substitute.For<MongoDbCollectionContext<AuctionDocument>>();
        mongoContext.GetCollection<AuctionDocument>().Returns(auctionCollection);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var sut = new AuctionFinalizationUnitOfWork(mongoContext, publishEndpoint, NullLogger<AuctionFinalizationUnitOfWork>.Instance);

        return new Fixture(mongoContext, auctionCollection, publishEndpoint, sut);
    }

    // ── Not yet finalized — the conditional claim succeeds, publishes, returns true ──────

    [Fact]
    public async Task FinalizeAsync_WhenTheAuctionIsNotYetFinalized_ClaimsItPublishesAndReturnsTrue()
    {
        var fixture = BuildFixture();
        var auction = SampleAuction();
        var finishedEvent = SampleEvent(auction);

        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = auction.Id, Seller = "bob", Finished = true });

        var result = await fixture.Sut.FinalizeAsync(auction, finishedEvent, CancellationToken.None);

        Assert.True(result);
        await fixture.PublishEndpoint.Received(1).Publish(finishedEvent, Arg.Any<CancellationToken>());
        await fixture.MongoContext.Received(1).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().AbortTransaction(Arg.Any<CancellationToken>());
    }

    // ── Already finalized by a concurrent pass — no publish, returns false, still commits ──

    [Fact]
    public async Task FinalizeAsync_WhenAConcurrentPassAlreadyFinalizedIt_PublishesNothingAndReturnsFalse()
    {
        var fixture = BuildFixture();
        var auction = SampleAuction();
        var finishedEvent = SampleEvent(auction);

        // Null = the conditional filter (Finished == false) did not match — a concurrent pass
        // already won this race.
        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns((AuctionDocument)null!);

        var result = await fixture.Sut.FinalizeAsync(auction, finishedEvent, CancellationToken.None);

        Assert.False(result);
        await fixture.PublishEndpoint.DidNotReceive().Publish(Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>());
        // Still a clean, committed no-op — not an error, not an abort.
        await fixture.MongoContext.Received(1).CommitTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().AbortTransaction(Arg.Any<CancellationToken>());
    }

    // ── An unexpected failure still aborts and propagates ────────────────────────────────

    [Fact]
    public async Task FinalizeAsync_WhenPublishFails_AbortsTheTransactionAndPropagates()
    {
        var fixture = BuildFixture();
        var auction = SampleAuction();
        var finishedEvent = SampleEvent(auction);

        fixture.AuctionCollection
            .Lock(Arg.Any<FilterDefinition<AuctionDocument>>(), Arg.Any<UpdateDefinition<AuctionDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new AuctionDocument { Id = auction.Id, Seller = "bob", Finished = true });
        fixture.PublishEndpoint.Publish(Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("RabbitMQ unreachable")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Sut.FinalizeAsync(auction, finishedEvent, CancellationToken.None));

        await fixture.MongoContext.Received(1).AbortTransaction(Arg.Any<CancellationToken>());
        await fixture.MongoContext.DidNotReceive().CommitTransaction(Arg.Any<CancellationToken>());
    }
}
