using AuctionService.Application.Consumers;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="BidRemovedConsumer"/> (Phase 11 Task 3.6): refreshes
/// <c>Auction.CurrentHighBid</c> from the event's already-recalculated value, and is
/// idempotent under redelivery.
/// </summary>
public class BidRemovedConsumerTests
{
    private static Auction SampleAuction(int? currentHighBid) => new()
    {
        Id = Guid.NewGuid(),
        Seller = "bob",
        SellerEmail = "bob@apexautobid.local",
        CurrentHighBid = currentHighBid,
        AuctionEnd = DateTime.UtcNow.AddDays(7),
        Status = Status.Live,
        Item = new Item { Make = "Ford", Model = "GT", Color = "Red", Year = 2020, Mileage = 1000 }
    };

    private static ConsumeContext<BidRemoved> BuildContext(BidRemoved message)
    {
        var context = Substitute.For<ConsumeContext<BidRemoved>>();
        context.Message.Returns(message);
        return context;
    }

    [Fact]
    public async Task Consume_RefreshesCurrentHighBidToEventValue()
    {
        var auction = SampleAuction(currentHighBid: 500);
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var sut = new BidRemovedConsumer(repository, NullLogger<BidRemovedConsumer>.Instance);

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), auction.Id.ToString(), 300)));

        Assert.Equal(300, auction.CurrentHighBid);
        await repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Consume_WhenEventCurrentHighBidIsNull_SetsCurrentHighBidToNull()
    {
        var auction = SampleAuction(currentHighBid: 500);
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var sut = new BidRemovedConsumer(repository, NullLogger<BidRemovedConsumer>.Instance);

        // The removed bid was the only bid — Bidding recalculated no remaining high bid.
        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), auction.Id.ToString(), null)));

        Assert.Null(auction.CurrentHighBid);
    }

    [Fact]
    public async Task Consume_IsIdempotentUnderRedelivery()
    {
        var auction = SampleAuction(currentHighBid: 500);
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var sut = new BidRemovedConsumer(repository, NullLogger<BidRemovedConsumer>.Instance);
        var message = new BidRemoved(Guid.NewGuid().ToString(), auction.Id.ToString(), 300);

        await sut.Consume(BuildContext(message));
        await sut.Consume(BuildContext(message));

        Assert.Equal(300, auction.CurrentHighBid);
    }

    [Fact]
    public async Task Consume_WhenAuctionIdUnparsable_SkipsWithoutThrowing()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var sut = new BidRemovedConsumer(repository, NullLogger<BidRemovedConsumer>.Instance);

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), "not-a-guid", 300)));

        await repository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>());
        await repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Consume_WhenAuctionNotFound_SkipsWithoutThrowing()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var missingId = Guid.NewGuid();
        repository.GetByIdAsync(missingId).Returns((Auction?)null);
        var sut = new BidRemovedConsumer(repository, NullLogger<BidRemovedConsumer>.Instance);

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), missingId.ToString(), 300)));

        await repository.DidNotReceive().SaveChangesAsync();
    }
}
