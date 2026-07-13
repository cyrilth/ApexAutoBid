using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AdminAuctionAppService"/> (Phase 11 Task 3.2/3.3/3.7/3.9):
/// end/cancel write the correct AuditEntry and emit the correct event, and stats aggregates
/// counts by status.
/// </summary>
public class AdminAuctionServiceTests
{
    private static Auction SampleAuction(Status status = Status.Live) => new()
    {
        Id = Guid.NewGuid(),
        Seller = "bob",
        SellerEmail = "bob@apexautobid.local",
        AuctionEnd = DateTime.UtcNow.AddDays(7),
        Status = status,
        Item = new Item
        {
            Make = "Ford",
            Model = "GT",
            Color = "Red",
            Year = 2020,
            Mileage = 1000,
            Images = [new ItemImage { Url = "http://ext/img.jpg", SortOrder = 0 }]
        }
    };

    private static AdminAuctionAppService BuildSut(
        IAuctionRepository repository, IMapper? mapper = null, IPublishEndpoint? publishEndpoint = null) =>
        new(
            repository,
            mapper ?? Substitute.For<IMapper>(),
            publishEndpoint ?? Substitute.For<IPublishEndpoint>(),
            NullLogger<AdminAuctionAppService>.Instance);

    // ── 3.2 — EndAuctionAsync ────────────────────────────────────────────────

    [Fact]
    public async Task EndAuctionAsync_WhenFound_SetsAuctionEndToNowAndEmitsAuctionUpdated()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint: publishEndpoint);
        var before = DateTime.UtcNow;

        var result = await sut.EndAuctionAsync(auction.Id, "admin1");

        Assert.Equal(AdminAuctionWriteResult.Success, result);
        Assert.True(auction.AuctionEnd >= before);
        await publishEndpoint.Received(1).Publish(Arg.Any<AuctionUpdated>(), Arg.Any<CancellationToken>());
        await repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task EndAuctionAsync_WhenFound_WritesAuditEntry()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var sut = BuildSut(repository);

        await sut.EndAuctionAsync(auction.Id, "admin1");

        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e =>
            e.Action == "AuctionEndedByAdmin"
            && e.EntityType == "Auction"
            && e.EntityId == auction.Id.ToString()
            && e.Actor == "admin1"
            && e.ActorIsAdmin));
    }

    [Fact]
    public async Task EndAuctionAsync_WhenNotFound_ReturnsNotFoundWithoutPublishing()
    {
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Auction?)null);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint: publishEndpoint);

        var result = await sut.EndAuctionAsync(Guid.NewGuid(), "admin1");

        Assert.Equal(AdminAuctionWriteResult.NotFound, result);
        await publishEndpoint.DidNotReceive().Publish(Arg.Any<AuctionUpdated>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SaveChangesAsync();
    }

    // ── 3.3 — CancelAuctionAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CancelAuctionAsync_WhenFound_SetsStatusCancelledAndEmitsAuctionCancelled()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint: publishEndpoint);

        var result = await sut.CancelAuctionAsync(auction.Id, "admin1");

        Assert.Equal(AdminAuctionWriteResult.Success, result);
        Assert.Equal(Status.Cancelled, auction.Status);
        await publishEndpoint.Received(1).Publish(
            Arg.Is<AuctionCancelled>(e => e.AuctionId == auction.Id.ToString() && e.Seller == "bob"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAuctionAsync_WhenFound_WritesAuditEntry()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var sut = BuildSut(repository);

        await sut.CancelAuctionAsync(auction.Id, "admin1");

        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e =>
            e.Action == "AuctionCancelledByAdmin"
            && e.EntityType == "Auction"
            && e.EntityId == auction.Id.ToString()
            && e.Actor == "admin1"
            && e.ActorIsAdmin));
    }

    [Fact]
    public async Task CancelAuctionAsync_WhenNotFound_ReturnsNotFound()
    {
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Auction?)null);
        var sut = BuildSut(repository);

        var result = await sut.CancelAuctionAsync(Guid.NewGuid(), "admin1");

        Assert.Equal(AdminAuctionWriteResult.NotFound, result);
    }

    // ── 3.7 — GetStatsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsTotalAndEveryStatusIncludingZeroCounts()
    {
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetStatusCountsAsync().Returns(new Dictionary<Status, int>
        {
            [Status.Live] = 5,
            [Status.Finished] = 2
        });
        var sut = BuildSut(repository);

        var stats = await sut.GetStatsAsync();

        Assert.Equal(7, stats.Total);
        Assert.Equal(5, stats.ByStatus["Live"]);
        Assert.Equal(2, stats.ByStatus["Finished"]);
        Assert.Equal(0, stats.ByStatus["ReserveNotMet"]);
        Assert.Equal(0, stats.ByStatus["Cancelled"]);
    }
}
