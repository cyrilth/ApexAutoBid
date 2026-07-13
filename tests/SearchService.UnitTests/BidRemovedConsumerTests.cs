using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SearchService.Application.Consumers;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;
using Xunit;

namespace SearchService.UnitTests;

/// <summary>
/// Unit tests for <see cref="BidRemovedConsumer"/>. The unit seam is the consumer itself:
/// <see cref="IItemRepository"/> and <see cref="ConsumeContext{T}"/> are both substituted,
/// mirroring <c>SearchAppServiceTests</c>' substitute-the-dependency style — no real
/// MongoDB/RabbitMQ involved (that coverage belongs to the Testcontainers-backed integration
/// tests).
/// </summary>
public class BidRemovedConsumerTests
{
    private readonly IItemRepository _repository = Substitute.For<IItemRepository>();

    private BidRemovedConsumer BuildSut() =>
        new(_repository, NullLogger<BidRemovedConsumer>.Instance);

    private static ConsumeContext<BidRemoved> BuildContext(
        BidRemoved message, CancellationToken cancellationToken)
    {
        var context = Substitute.For<ConsumeContext<BidRemoved>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    private static Item LiveItem(Guid id) => new()
    {
        Id = id,
        CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
        AuctionEnd = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
        Seller = "bob",
        Winner = null,
        Make = "Ford",
        Model = "GT",
        Year = 2020,
        Color = "Red",
        Mileage = 1000,
        ImageUrl = "http://images.local/ford-gt.jpg",
        ThumbnailUrl = null,
        Status = "Live",
        ReservePrice = 20000,
        SoldAmount = null,
        CurrentHighBid = 25000
    };

    [Fact]
    public async Task Consume_WhenItemFound_SetsCurrentHighBidToTheEventValue()
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), id.ToString(), 18000), CancellationToken.None));

        Assert.Equal(18000, item.CurrentHighBid);
        await _repository.Received(1).UpsertAsync(
            Arg.Is<Item>(i => i.Id == id && i.CurrentHighBid == 18000), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenNoBidsRemain_SetsCurrentHighBidToNull()
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), id.ToString(), null), CancellationToken.None));

        Assert.Null(item.CurrentHighBid);
        await _repository.Received(1).UpsertAsync(
            Arg.Is<Item>(i => i.Id == id && i.CurrentHighBid == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_IsIdempotent_RedeliveryReappliesTheSameValue()
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        item.CurrentHighBid = 18000;
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), id.ToString(), 18000), CancellationToken.None));

        Assert.Equal(18000, item.CurrentHighBid);
        await _repository.Received(1).UpsertAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenItemNotFound_LogsAndThrowsSoMassTransitRetries()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Item?)null);
        var sut = BuildSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), id.ToString(), 1000), CancellationToken.None)));

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenAuctionIdUnparsable_SkipsWithoutTouchingTheRepository()
    {
        var sut = BuildSut();

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), "not-a-guid", 1000), CancellationToken.None));

        await _repository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PropagatesTheCallersCancellationTokenToTheRepositoryUnchanged()
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        using var cts = new CancellationTokenSource();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new BidRemoved(Guid.NewGuid().ToString(), id.ToString(), 5000), cts.Token));

        await _repository.Received().GetByIdAsync(id, cts.Token);
        await _repository.Received().UpsertAsync(Arg.Any<Item>(), cts.Token);
    }
}
