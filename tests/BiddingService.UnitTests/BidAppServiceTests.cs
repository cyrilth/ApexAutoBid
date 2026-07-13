using BiddingService.Application.DTOs;
using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using Contracts;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="BidAppService"/> (Phase 5 — coverage areas 15.1–15.7 plus the
/// reserve-price boundary). The unit seam is <see cref="BidAppService"/> itself:
/// <see cref="IBidRepository"/>, <see cref="IAuctionProvider"/>, and
/// <see cref="IBidPlacementUnitOfWork"/> are all substituted, while the real Mapster
/// <c>BidMappingConfig</c> (scanned exactly like <c>ApplicationServiceExtensions</c> does) is
/// exercised for real so the Bid → BidDto / Bid → Contracts.BidPlaced mappings are genuinely
/// pinned, not assumed — mirrors <c>SearchAppServiceTests</c>' identical convention. Mongo-,
/// MassTransit-, and gRPC-dependent behavior belongs to the Task 16 integration tests, not here.
/// </summary>
public class BidAppServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

    // Real Mapster config, scanned from the Application assembly exactly like
    // ApplicationServiceExtensions.AddApplicationServices — so BidMappingConfig's
    // Bid -> BidDto / Bid -> BidPlaced rules are genuinely exercised, not mocked away.
    private static IMapper BuildMapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(BidAppService).Assembly);
        return new Mapper(config);
    }

    private static BidAppService BuildSut(
        IBidRepository bidRepository,
        IAuctionProvider auctionProvider,
        IBidPlacementUnitOfWork placementUnitOfWork,
        DateTimeOffset? now = null) =>
        new(
            bidRepository,
            auctionProvider,
            placementUnitOfWork,
            BuildMapper(),
            new FixedTimeProvider(now ?? FixedNow),
            NullLogger<BidAppService>.Instance);

    private static Auction SampleAuction(
        Guid? id = null,
        string seller = "bob",
        int reservePrice = 20000,
        DateTime? auctionEnd = null,
        bool finished = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Seller = seller,
            ReservePrice = reservePrice,
            AuctionEnd = auctionEnd ?? FixedNow.UtcDateTime.AddDays(7),
            Finished = finished
        };

    private static IAuctionProvider ProviderReturning(Auction? auction, Guid? forAuctionId = null)
    {
        var provider = Substitute.For<IAuctionProvider>();
        provider.GetAuctionAsync(forAuctionId ?? auction?.Id ?? Guid.Empty, Arg.Any<CancellationToken>())
            .Returns(auction);
        return provider;
    }

    private static IBidRepository RepositoryWithCurrentHighBid(Guid auctionId, int? currentHighBid)
    {
        var repository = Substitute.For<IBidRepository>();
        repository.GetHighestAcceptedAmountAsync(auctionId, Arg.Any<CancellationToken>()).Returns(currentHighBid);
        return repository;
    }

    // ── 15.1 — valid bid returns Accepted ────────────────────────────────────────

    [Fact]
    public async Task PlaceBidAsync_WhenAmountBeatsCurrentHighBidAndMeetsReserve_ReturnsAcceptedAndPublishesBidPlaced()
    {
        var auction = SampleAuction(reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 25000 };

        var (outcome, bid) = await sut.PlaceBidAsync(
            dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.NotNull(bid);
        Assert.Equal("Accepted", bid!.BidStatus);
        Assert.Equal(auction.Id, bid.AuctionId);
        Assert.Equal("alice", bid.Bidder);
        Assert.Equal(25000, bid.Amount);
        Assert.Equal(FixedNow.UtcDateTime, bid.BidTime);
        Assert.NotEqual(Guid.Empty, bid.Id);

        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Is<Bid>(b =>
                b.BidStatus == BidStatus.Accepted &&
                b.Amount == 25000 &&
                b.Bidder == "alice" &&
                b.BidderEmail == "alice@apexautobid.local"),
            Arg.Is<BidPlaced?>(e => e != null && e.BidStatus == "Accepted" && e.Amount == 25000),
            Arg.Any<CancellationToken>());
    }

    // ── Reserve boundary — amount == reserve is still Accepted (>=, not >) ───────

    [Fact]
    public async Task PlaceBidAsync_WhenAmountExactlyEqualsReservePrice_ReturnsAccepted()
    {
        var auction = SampleAuction(reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 20000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("Accepted", bid!.BidStatus);
    }

    // ── 15.2 — bid below reserve returns AcceptedBelowReserve ────────────────────

    [Fact]
    public async Task PlaceBidAsync_WhenAmountBeatsCurrentHighBidButBelowReserve_ReturnsAcceptedBelowReserveAndStillPublishes()
    {
        var auction = SampleAuction(reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 15000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("AcceptedBelowReserve", bid!.BidStatus);

        // AcceptedBelowReserve is one of the two statuses that still counts as a genuine new
        // high bid worth broadcasting (BidAppService's own remarks) — the event must still be
        // published, unlike TooLow/Finished.
        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Any<Bid>(),
            Arg.Is<BidPlaced?>(e => e != null && e.BidStatus == "AcceptedBelowReserve"),
            Arg.Any<CancellationToken>());
    }

    // ── 15.3 — bid too low returns TooLow ─────────────────────────────────────────

    [Theory]
    [InlineData(20000, 20000)] // equal to current high bid — "<=" means still TooLow
    [InlineData(20000, 10000)] // strictly below current high bid
    public async Task PlaceBidAsync_WhenAmountDoesNotBeatCurrentHighBid_ReturnsTooLowAndDoesNotPublish(
        int currentHighBid, int amount)
    {
        var auction = SampleAuction(reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = amount };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("TooLow", bid!.BidStatus);

        // TooLow is still recorded (Requirements §3.3) but never broadcast — null event.
        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Is<Bid>(b => b.BidStatus == BidStatus.TooLow), null, Arg.Any<CancellationToken>());
    }

    // ── 15.4 — auction finished returns Finished ─────────────────────────────────

    [Fact]
    public async Task PlaceBidAsync_WhenAuctionAlreadyMarkedFinished_ReturnsFinishedRegardlessOfAmount()
    {
        var auction = SampleAuction(reservePrice: 20000, finished: true);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        // A huge amount that would otherwise clearly be Accepted — Finished must win regardless.
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 999_000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("Finished", bid!.BidStatus);
        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Is<Bid>(b => b.BidStatus == BidStatus.Finished), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceBidAsync_WhenNowIsAtOrPastAuctionEnd_ReturnsFinishedEvenWhenFinishedFlagIsFalse()
    {
        // Finished=false but AuctionEnd has already passed "now" — BidAppService treats an
        // auction as ended when EITHER condition holds (its own remarks).
        var auction = SampleAuction(
            reservePrice: 20000, finished: false, auctionEnd: FixedNow.UtcDateTime.AddMinutes(-1));
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 25000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("Finished", bid!.BidStatus);
    }

    // ── 9.3 — a cancelled auction (AuctionCancelledConsumer sets Finished = true) refuses bids ──
    //
    // Cancellation is represented locally by exactly the same Auction.Finished flag a normal
    // auction end sets (see AuctionCancelledConsumer's own remarks for why no separate
    // "Cancelled" state is needed) — so this is mechanically identical to
    // PlaceBidAsync_WhenAuctionAlreadyMarkedFinished_ReturnsFinishedRegardlessOfAmount above;
    // kept as its own named test for Task 9.3's traceability.

    [Fact]
    public async Task PlaceBidAsync_WhenTheAuctionWasCancelled_ReturnsFinishedAndRecordsNoAcceptedBid()
    {
        // AuctionCancelledConsumer.Consume calls IAuctionRepository.MarkFinishedAsync, which is
        // exactly Auction.Finished = true from BidAppService's point of view — it has no
        // separate notion of "cancelled" at all.
        var auction = SampleAuction(reservePrice: 20000, finished: true);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 30000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("Finished", bid!.BidStatus);
        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Is<Bid>(b => b.BidStatus == BidStatus.Finished),
            null, // never published — a Finished bid is recorded but never broadcast
            Arg.Any<CancellationToken>());
    }

    // ── 15.5 — bidder is the seller → the 400 path (BidOutcome.SellerCannotBid) ──

    [Fact]
    public async Task PlaceBidAsync_WhenBidderIsTheAuctionsOwnSeller_ReturnsSellerCannotBidWithoutRecordingABid()
    {
        var auction = SampleAuction(seller: "bob", reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = Substitute.For<IBidRepository>();
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 25000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "bob", "bob@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.SellerCannotBid, outcome);
        Assert.Null(bid);

        // Short-circuits before any status determination or persistence — the seller check
        // happens before DetermineStatusAsync's own repository call.
        await bidRepository.DidNotReceive().GetHighestAcceptedAmountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await placementUnitOfWork.DidNotReceive().SaveAsync(
            Arg.Any<Bid>(), Arg.Any<BidPlaced?>(), Arg.Any<CancellationToken>());
    }

    // ── 15.6 — auction not found triggers the IAuctionProvider fallback seam ─────
    //
    // BidAppService itself has no knowledge of gRPC/Mongo — it only ever talks to
    // IAuctionProvider. Both branches of that seam are exercised directly here: a local miss
    // (provider returns null) surfaces as AuctionNotFound with nothing recorded, and a provider
    // that resolves the auction (whether from the local store or, later, the gRPC fallback —
    // BidAppService cannot tell the difference and must not need to) lets the bid proceed
    // exactly as it would for any other resolvable auction.

    [Fact]
    public async Task PlaceBidAsync_WhenAuctionProviderReturnsNull_ReturnsAuctionNotFoundWithoutRecordingABid()
    {
        var auctionId = Guid.NewGuid();
        var auctionProvider = ProviderReturning(null, forAuctionId: auctionId);
        var bidRepository = Substitute.For<IBidRepository>();
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auctionId, Amount = 25000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.AuctionNotFound, outcome);
        Assert.Null(bid);
        await placementUnitOfWork.DidNotReceive().SaveAsync(
            Arg.Any<Bid>(), Arg.Any<BidPlaced?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceBidAsync_WhenAuctionProviderResolvesTheAuctionAfterALocalMiss_BidProceedsNormally()
    {
        // Models the gRPC-fallback seam: whatever IAuctionProvider implementation is registered
        // (LocalAuctionProvider today, GrpcFallbackAuctionProvider in Program.cs), once it
        // resolves an Auction — even after its own internal local miss — BidAppService treats
        // it exactly like any other successful lookup.
        var auction = SampleAuction(reservePrice: 20000);
        var auctionProvider = ProviderReturning(auction);
        var bidRepository = RepositoryWithCurrentHighBid(auction.Id, currentHighBid: null);
        var placementUnitOfWork = Substitute.For<IBidPlacementUnitOfWork>();
        var sut = BuildSut(bidRepository, auctionProvider, placementUnitOfWork);
        var dto = new PlaceBidDto { AuctionId = auction.Id, Amount = 25000 };

        var (outcome, bid) = await sut.PlaceBidAsync(dto, "alice", "alice@apexautobid.local", CancellationToken.None);

        Assert.Equal(BidOutcome.Placed, outcome);
        Assert.Equal("Accepted", bid!.BidStatus);
        await placementUnitOfWork.Received(1).SaveAsync(
            Arg.Any<Bid>(), Arg.Any<BidPlaced?>(), Arg.Any<CancellationToken>());
    }

    // ── 15.7 — GetBids returns bids for auction (ordering + no BidderEmail) ──────

    [Fact]
    public async Task GetBidsForAuctionAsync_ReturnsBidsInRepositoryOrderWithoutExposingBidderEmail()
    {
        var auctionId = Guid.NewGuid();
        var bids = new List<Bid>
        {
            new()
            {
                Id = Guid.NewGuid(), AuctionId = auctionId, Bidder = "tom",
                BidderEmail = "tom@apexautobid.local", BidTime = FixedNow.UtcDateTime.AddMinutes(-5),
                Amount = 18000, BidStatus = BidStatus.AcceptedBelowReserve
            },
            new()
            {
                Id = Guid.NewGuid(), AuctionId = auctionId, Bidder = "alice",
                BidderEmail = "alice@apexautobid.local", BidTime = FixedNow.UtcDateTime.AddMinutes(-10),
                Amount = 15000, BidStatus = BidStatus.AcceptedBelowReserve
            },
        };
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetByAuctionIdAsync(auctionId, Arg.Any<CancellationToken>()).Returns(bids);
        var sut = BuildSut(
            bidRepository, Substitute.For<IAuctionProvider>(), Substitute.For<IBidPlacementUnitOfWork>());

        var result = await sut.GetBidsForAuctionAsync(auctionId, CancellationToken.None);

        Assert.Equal(2, result.Count);

        // Ordering is IBidRepository's own responsibility (newest-first — see its remarks); the
        // Application layer must preserve whatever order it receives, not re-sort.
        Assert.Equal(bids[0].Id, result[0].Id);
        Assert.Equal(bids[1].Id, result[1].Id);
        Assert.Equal("tom", result[0].Bidder);
        Assert.Equal(18000, result[0].Amount);
        Assert.Equal("AcceptedBelowReserve", result[0].BidStatus);
        Assert.Equal(bids[0].BidTime, result[0].BidTime);
        Assert.Equal("alice", result[1].Bidder);

        // BidderEmail must never be exposed — BidDto simply carries no such property at all
        // (Requirements §3.3); guard against a future accidental re-introduction.
        Assert.DoesNotContain(typeof(BidDto).GetProperties(), p => p.Name == "BidderEmail");
    }
}
