using BiddingService.Application.Configuration;
using BiddingService.Application.Consumers;
using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// End-to-end (still fully in-process, no real MongoDB) unit test for Phase 11 Task 5.2/9.6's
/// "the finalization background job NEVER emits AuctionFinished for a cancelled auction". Wires
/// the real <see cref="AuctionCancelledConsumer"/> and the real <see cref="AuctionFinalizationAppService"/>
/// together against a single small in-memory <see cref="FakeAuctionRepository"/> that mirrors
/// the real <c>AuctionRepository</c>'s own <c>GetExpiredUnfinalizedAsync</c> filter shape
/// (<c>!Finished &amp;&amp; AuctionEnd &lt;= asOf</c>) — demonstrating the emergent guarantee
/// directly, rather than only asserting each piece in isolation.
/// </summary>
public class AuctionCancellationFinalizerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Minimal in-memory <see cref="IAuctionRepository"/> double that mirrors the real Mongo
    /// repository's own filter semantics for exactly the two operations this test exercises —
    /// not a general-purpose test double for the rest of this suite.
    /// </summary>
    private sealed class FakeAuctionRepository : IAuctionRepository
    {
        private readonly Dictionary<Guid, Auction> _auctions = [];

        public void Seed(Auction auction) => _auctions[auction.Id] = auction;

        public Task<Auction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_auctions.GetValueOrDefault(id));

        public Task InsertIfNotExistsAsync(Auction auction, CancellationToken cancellationToken)
        {
            _auctions.TryAdd(auction.Id, auction);
            return Task.CompletedTask;
        }

        public Task<List<Auction>> GetExpiredUnfinalizedAsync(DateTime asOf, CancellationToken cancellationToken) =>
            Task.FromResult(_auctions.Values.Where(a => !a.Finished && a.AuctionEnd <= asOf).ToList());

        public Task MarkFinishedAsync(Guid auctionId, CancellationToken cancellationToken)
        {
            if (_auctions.TryGetValue(auctionId, out var auction))
                auction.Finished = true;
            return Task.CompletedTask;
        }

        public Task UpdateAuctionEndAsync(Guid auctionId, DateTime auctionEnd, CancellationToken cancellationToken)
        {
            if (_auctions.TryGetValue(auctionId, out var auction))
                auction.AuctionEnd = auctionEnd;
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<AuctionCancelled> BuildCancelledContext(AuctionCancelled message)
    {
        var context = Substitute.For<ConsumeContext<AuctionCancelled>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static AuctionFinalizationAppService BuildFinalizer(
        IAuctionRepository auctionRepository, IAuctionFinalizationUnitOfWork unitOfWork) =>
        new(
            auctionRepository,
            Substitute.For<IBidRepository>(),
            unitOfWork,
            new FinalizationFailureTracker(),
            Options.Create(new FinalizationOptions { FinalizationGraceSeconds = 0 }),
            new FixedTimeProvider(FixedNow),
            NullLogger<AuctionFinalizationAppService>.Instance);

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_NeverFinalizesAnAuctionThatAuctionCancelledMarkedFinished()
    {
        var auctionId = Guid.NewGuid();
        var repository = new FakeAuctionRepository();
        repository.Seed(new Auction
        {
            Id = auctionId,
            Seller = "bob",
            ReservePrice = 20000,
            // Already past AuctionEnd — exactly the real-world case the finalizer exists to
            // pick up, were it not for the cancellation below.
            AuctionEnd = FixedNow.UtcDateTime.AddDays(-1),
            Finished = false
        });

        // The Auction Service's AuctionCancelled arrives and is consumed BEFORE the finalizer's
        // next tick.
        var consumer = new AuctionCancelledConsumer(repository, NullLogger<AuctionCancelledConsumer>.Instance);
        await consumer.Consume(BuildCancelledContext(new AuctionCancelled(auctionId.ToString(), "bob")));

        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        var finalizer = BuildFinalizer(repository, unitOfWork);

        await finalizer.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        // GetExpiredUnfinalizedAsync's own !Finished filter now permanently excludes this
        // auction — the finalizer never even attempts it, so AuctionFinished can never be
        // published for it.
        await unitOfWork.DidNotReceive().FinalizeAsync(
            Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_StillFinalizesAnExpiredAuctionThatWasNeverCancelled()
    {
        // Control case — proves the fake repository's filter (and this test's setup) is
        // otherwise unremarkable: an expired, non-cancelled auction IS finalized as normal.
        var auctionId = Guid.NewGuid();
        var repository = new FakeAuctionRepository();
        repository.Seed(new Auction
        {
            Id = auctionId,
            Seller = "bob",
            ReservePrice = 20000,
            AuctionEnd = FixedNow.UtcDateTime.AddDays(-1),
            Finished = false
        });

        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var finalizer = BuildFinalizer(repository, unitOfWork);

        await finalizer.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await unitOfWork.Received(1).FinalizeAsync(
            Arg.Is<Auction>(a => a.Id == auctionId), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>());
    }
}
