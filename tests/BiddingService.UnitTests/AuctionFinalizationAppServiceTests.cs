using BiddingService.Application.Configuration;
using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AuctionFinalizationAppService"/> (Phase 5 Tasks 11/12) — the
/// background finalizer's winner semantics: a highest bid that is strictly
/// <see cref="BidStatus.Accepted"/> sells the auction to that bidder; a highest bid that is
/// only ever <see cref="BidStatus.AcceptedBelowReserve"/> does not sell (Requirements
/// §3.3/§8.3). <see cref="IAuctionRepository"/>/<see cref="IBidRepository"/>/
/// <see cref="IAuctionFinalizationUnitOfWork"/> are all substituted — Mongo/MassTransit
/// transaction mechanics belong to the Task 16 integration tests, not here.
/// </summary>
public class AuctionFinalizationAppServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

    private static AuctionFinalizationAppService BuildSut(
        IAuctionRepository auctionRepository,
        IBidRepository bidRepository,
        IAuctionFinalizationUnitOfWork unitOfWork,
        DateTimeOffset? now = null,
        int graceSeconds = 0,
        IFinalizationFailureTracker? failureTracker = null,
        ILogger<AuctionFinalizationAppService>? logger = null) =>
        new(
            auctionRepository,
            bidRepository,
            unitOfWork,
            failureTracker ?? new FinalizationFailureTracker(),
            Options.Create(new FinalizationOptions { FinalizationGraceSeconds = graceSeconds }),
            new FixedTimeProvider(now ?? FixedNow),
            logger ?? NullLogger<AuctionFinalizationAppService>.Instance);

    private static Auction SampleAuction(Guid? id = null, string seller = "bob") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Seller = seller,
        ReservePrice = 20000,
        AuctionEnd = FixedNow.UtcDateTime.AddDays(-1),
        Finished = false
    };

    // ── Winner semantics: highest strictly-Accepted bid wins ─────────────────────

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenHighestBidIsStrictlyAccepted_PublishesItemSoldWithWinnerAndAmount()
    {
        var auction = SampleAuction(seller: "bob");
        var winningBid = new Bid
        {
            Id = Guid.NewGuid(), AuctionId = auction.Id, Bidder = "alice",
            BidderEmail = "alice@apexautobid.local", Amount = 25000,
            BidStatus = BidStatus.Accepted, BidTime = FixedNow.UtcDateTime.AddMinutes(-5)
        };

        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([auction]);
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetHighestAcceptedBidAsync(auction.Id, Arg.Any<CancellationToken>()).Returns(winningBid);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await unitOfWork.Received(1).FinalizeAsync(
            Arg.Is<Auction>(a => a.Id == auction.Id && a.Finished),
            Arg.Is<AuctionFinished>(e =>
                e.ItemSold &&
                e.AuctionId == auction.Id.ToString() &&
                e.Winner == "alice" &&
                e.WinnerEmail == "alice@apexautobid.local" &&
                e.Seller == "bob" &&
                e.Amount == 25000),
            Arg.Any<CancellationToken>());
    }

    // ── Winner semantics: AcceptedBelowReserve-only high bid → not sold, no WinnerEmail ──

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenHighestBidIsOnlyAcceptedBelowReserve_PublishesNotSoldWithNoWinnerOrEmail()
    {
        var auction = SampleAuction(seller: "tom");

        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([auction]);
        var bidRepository = Substitute.For<IBidRepository>();
        // GetHighestAcceptedBidAsync only ever returns a strictly-Accepted bid — per its own
        // contract, an auction whose high bid is merely AcceptedBelowReserve yields null here.
        bidRepository.GetHighestAcceptedBidAsync(auction.Id, Arg.Any<CancellationToken>())
            .Returns((Bid?)null);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await unitOfWork.Received(1).FinalizeAsync(
            Arg.Is<Auction>(a => a.Id == auction.Id && a.Finished),
            Arg.Is<AuctionFinished>(e =>
                !e.ItemSold &&
                e.AuctionId == auction.Id.ToString() &&
                e.Winner == null &&
                e.WinnerEmail == null &&
                e.Seller == "tom" &&
                e.Amount == null),
            Arg.Any<CancellationToken>());
    }

    // ── No expired auctions → nothing finalized ──────────────────────────────────

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenNoAuctionsAreExpired_NeverCallsTheUnitOfWork()
    {
        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var bidRepository = Substitute.For<IBidRepository>();
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await bidRepository.DidNotReceive().GetHighestAcceptedBidAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().FinalizeAsync(
            Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>());
    }

    // ── One auction failing to finalize must not stop the rest of the batch ─────

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenOneAuctionFailsToFinalize_StillFinalizesTheOthersAndSwallowsTheException()
    {
        var failingAuction = SampleAuction(seller: "bob");
        var succeedingAuction = SampleAuction(seller: "alice");

        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([failingAuction, succeedingAuction]);
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetHighestAcceptedBidAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Bid?)null);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(
                Arg.Is<Auction>(a => a.Id == succeedingAuction.Id),
                Arg.Any<AuctionFinished>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        unitOfWork.FinalizeAsync(
                Arg.Is<Auction>(a => a.Id == failingAuction.Id),
                Arg.Any<AuctionFinished>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient Mongo/RabbitMQ blip"));

        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork);

        // Must not throw — a single auction's failure is logged and contained (Task 12's
        // "survive transient errors" requirement); it never propagates out of this call.
        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await unitOfWork.Received(1).FinalizeAsync(
            Arg.Is<Auction>(a => a.Id == succeedingAuction.Id),
            Arg.Any<AuctionFinished>(),
            Arg.Any<CancellationToken>());
    }

    // ── Uses the injected clock, not the wall clock, for the expiry cutoff ───────

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WithZeroGracePeriod_PassesTheInjectedTimeProvidersInstantAsTheExpiryCutoff()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var auctionRepository = Substitute.For<IAuctionRepository>();
        DateTime? captured = null;
        auctionRepository
            .GetExpiredUnfinalizedAsync(Arg.Do<DateTime>(d => captured = d), Arg.Any<CancellationToken>())
            .Returns([]);
        var bidRepository = Substitute.For<IBidRepository>();
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork, fixedNow, graceSeconds: 0);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        Assert.Equal(fixedNow.UtcDateTime, captured);
    }

    // ── Critical 2 — non-zero grace period shifts the cutoff back by that many seconds ──

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WithNonZeroGracePeriod_PassesNowMinusGraceAsTheExpiryCutoff()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        const int graceSeconds = 15;
        var auctionRepository = Substitute.For<IAuctionRepository>();
        DateTime? captured = null;
        auctionRepository
            .GetExpiredUnfinalizedAsync(Arg.Do<DateTime>(d => captured = d), Arg.Any<CancellationToken>())
            .Returns([]);
        var bidRepository = Substitute.For<IBidRepository>();
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork, fixedNow, graceSeconds);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        Assert.Equal(fixedNow.UtcDateTime.AddSeconds(-graceSeconds), captured);
    }

    // ── Critical 2 — the unit of work losing the atomic-finalize race is a no-op, not a failure ──

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenUnitOfWorkReportsAlreadyFinalized_DoesNotThrowAndIsTreatedAsSuccess()
    {
        var auction = SampleAuction(seller: "bob");
        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([auction]);
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetHighestAcceptedBidAsync(auction.Id, Arg.Any<CancellationToken>()).Returns((Bid?)null);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        // false = a concurrent pass already finalized it first (IAuctionFinalizationUnitOfWork's
        // own remarks) — a normal, idempotent no-op, never an exception.
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var failureTracker = Substitute.For<IFinalizationFailureTracker>();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork, failureTracker: failureTracker);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        await unitOfWork.Received(1).FinalizeAsync(
            Arg.Is<Auction>(a => a.Id == auction.Id), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>());
        // Warning 4's failure counter must NOT see this as a failure — RecordFailure is never
        // called, only RecordSuccess (a lost race is not a finalization failure).
        failureTracker.DidNotReceive().RecordFailure(Arg.Any<Guid>());
        failureTracker.Received(1).RecordSuccess(auction.Id);
    }

    // ── Warning 4 — consecutive per-auction failures escalate Warning -> Error ──────

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenSameAuctionFailsBelowThreshold_LogsWarningNotError()
    {
        var auction = SampleAuction(seller: "bob");
        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([auction]);
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetHighestAcceptedBidAsync(auction.Id, Arg.Any<CancellationToken>()).Returns((Bid?)null);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient Mongo/RabbitMQ blip"));
        var logger = Substitute.For<ILogger<AuctionFinalizationAppService>>();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork, logger: logger);

        // Below the 3-consecutive-failure threshold — first failure for this auction.
        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        var levels = logger.ReceivedCalls().Select(c => (LogLevel)c.GetArguments()[0]!).ToList();
        Assert.Contains(LogLevel.Warning, levels);
        Assert.DoesNotContain(LogLevel.Error, levels);
    }

    [Fact]
    public async Task FinalizeExpiredAuctionsAsync_WhenSameAuctionFailsOnThreeConsecutiveTicks_EscalatesToError()
    {
        var auctionId = Guid.NewGuid();
        var auctionRepository = Substitute.For<IAuctionRepository>();
        auctionRepository.GetExpiredUnfinalizedAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ => [SampleAuction(id: auctionId, seller: "bob")]);
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetHighestAcceptedBidAsync(auctionId, Arg.Any<CancellationToken>()).Returns((Bid?)null);
        var unitOfWork = Substitute.For<IAuctionFinalizationUnitOfWork>();
        unitOfWork.FinalizeAsync(Arg.Any<Auction>(), Arg.Any<AuctionFinished>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transient Mongo/RabbitMQ blip"));
        var logger = Substitute.For<ILogger<AuctionFinalizationAppService>>();
        // The SAME failure tracker instance must be reused across ticks — mirrors it being a
        // singleton in the real DI container even though AuctionFinalizationAppService itself
        // is scoped (see IFinalizationFailureTracker's own remarks).
        var failureTracker = new FinalizationFailureTracker();
        var sut = BuildSut(auctionRepository, bidRepository, unitOfWork, failureTracker: failureTracker, logger: logger);

        // Ticks 1 and 2 stay below the threshold; tick 3 must escalate to Error.
        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);
        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);
        Assert.DoesNotContain(
            logger.ReceivedCalls(), c => (LogLevel)c.GetArguments()[0]! == LogLevel.Error);

        await sut.FinalizeExpiredAuctionsAsync(CancellationToken.None);

        Assert.Contains(
            logger.ReceivedCalls(), c => (LogLevel)c.GetArguments()[0]! == LogLevel.Error);
    }
}
