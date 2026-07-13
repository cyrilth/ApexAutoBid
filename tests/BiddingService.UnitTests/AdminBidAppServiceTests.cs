using System.Text.Json;
using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AdminBidAppService"/> (Phase 11 Task 5.1/5.4/5.5 / Task 9.2/9.6) —
/// the admin bid-removal outcome mapping, the exact <see cref="AuditEntry"/> this service hands
/// to <see cref="IBidRemovalUnitOfWork"/>, and the stats read. <see cref="IBidRepository"/>/
/// <see cref="IBidRemovalUnitOfWork"/> are both substituted — Mongo/MassTransit transaction
/// mechanics belong to <see cref="BidRemovalUnitOfWorkTests"/>, not here (mirrors
/// <c>BidAppServiceTests</c>' identical "the unit seam is the app service itself" convention).
/// </summary>
public class AdminBidAppServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

    private static Bid SampleBid(Guid? id = null, Guid? auctionId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AuctionId = auctionId ?? Guid.NewGuid(),
        Bidder = "alice",
        BidderEmail = "alice@apexautobid.local",
        BidTime = new DateTime(2026, 6, 15, 11, 0, 0, DateTimeKind.Utc),
        Amount = 18000,
        BidStatus = BidStatus.Accepted
    };

    private static AdminBidAppService BuildSut(
        IBidRepository bidRepository, IBidRemovalUnitOfWork removalUnitOfWork) =>
        new(bidRepository, removalUnitOfWork, new FixedTimeProvider(FixedNow), NullLogger<AdminBidAppService>.Instance);

    // ── 9.2 — RemoveBid delegates to the unit of work with the correct bid and returns Removed ──

    [Fact]
    public async Task RemoveBidAsync_WhenTheBidExists_DelegatesToTheUnitOfWorkAndReturnsRemoved()
    {
        var bid = SampleBid();
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetByIdAsync(bid.Id, Arg.Any<CancellationToken>()).Returns(bid);
        var removalUnitOfWork = Substitute.For<IBidRemovalUnitOfWork>();
        removalUnitOfWork.RemoveAsync(Arg.Any<Bid>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(22000);
        var sut = BuildSut(bidRepository, removalUnitOfWork);

        var outcome = await sut.RemoveBidAsync(bid.Id, "admin", CancellationToken.None);

        Assert.Equal(RemoveBidOutcome.Removed, outcome);
        await removalUnitOfWork.Received(1).RemoveAsync(
            Arg.Is<Bid>(b => b.Id == bid.Id && b.AuctionId == bid.AuctionId),
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    // ── 404 — no bid with the given id exists ────────────────────────────────────

    [Fact]
    public async Task RemoveBidAsync_WhenNoBidExists_ReturnsNotFoundWithoutTouchingTheUnitOfWork()
    {
        var bidId = Guid.NewGuid();
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetByIdAsync(bidId, Arg.Any<CancellationToken>()).Returns((Bid?)null);
        var removalUnitOfWork = Substitute.For<IBidRemovalUnitOfWork>();
        var sut = BuildSut(bidRepository, removalUnitOfWork);

        var outcome = await sut.RemoveBidAsync(bidId, "admin", CancellationToken.None);

        Assert.Equal(RemoveBidOutcome.NotFound, outcome);
        await removalUnitOfWork.DidNotReceive().RemoveAsync(
            Arg.Any<Bid>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── 9.6 — the AuditEntry handed to the unit of work captures the removed bid's details ──

    [Fact]
    public async Task RemoveBidAsync_BuildsAnAuditEntryCapturingTheRemovedBidsBidderAmountTimeAndAuction()
    {
        var bid = SampleBid();
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.GetByIdAsync(bid.Id, Arg.Any<CancellationToken>()).Returns(bid);
        var removalUnitOfWork = Substitute.For<IBidRemovalUnitOfWork>();
        AuditEntry? capturedEntry = null;
        removalUnitOfWork
            .RemoveAsync(Arg.Any<Bid>(), Arg.Do<AuditEntry>(e => capturedEntry = e), Arg.Any<CancellationToken>())
            .Returns((int?)null);
        var sut = BuildSut(bidRepository, removalUnitOfWork);

        await sut.RemoveBidAsync(bid.Id, "admin-carol", CancellationToken.None);

        Assert.NotNull(capturedEntry);
        Assert.NotEqual(Guid.Empty, capturedEntry!.Id);
        Assert.Equal(FixedNow.UtcDateTime, capturedEntry.Timestamp);
        Assert.Equal("admin-carol", capturedEntry.Actor);
        Assert.True(capturedEntry.ActorIsAdmin);
        Assert.Equal("BidRemoved", capturedEntry.Action);
        Assert.Equal("Bid", capturedEntry.EntityType);
        Assert.Equal(bid.Id.ToString(), capturedEntry.EntityId);

        // Data is a JSON payload summarizing the removed bid — bidder, amount, time, auction —
        // never BidderEmail (Requirements §13.5). Property casing matches
        // AuctionAppService's identical convention: JsonSerializer.Serialize with no
        // JsonSerializerOptions preserves the anonymous type's own PascalCase member names.
        using var data = JsonDocument.Parse(capturedEntry.Data);
        Assert.Equal(bid.AuctionId.ToString(), data.RootElement.GetProperty("AuctionId").GetString());
        Assert.Equal("alice", data.RootElement.GetProperty("Bidder").GetString());
        Assert.Equal(18000, data.RootElement.GetProperty("Amount").GetInt32());
        Assert.Equal(bid.BidTime, data.RootElement.GetProperty("BidTime").GetDateTime());
        Assert.False(data.RootElement.TryGetProperty("BidderEmail", out _));
    }

    // ── GetStats — total bid count comes straight from the repository ───────────

    [Fact]
    public async Task GetStatsAsync_ReturnsTheRepositorysTotalBidCount()
    {
        var bidRepository = Substitute.For<IBidRepository>();
        bidRepository.CountAsync(Arg.Any<CancellationToken>()).Returns(42L);
        var sut = BuildSut(bidRepository, Substitute.For<IBidRemovalUnitOfWork>());

        var stats = await sut.GetStatsAsync(CancellationToken.None);

        Assert.Equal(42, stats.TotalBids);
    }
}
