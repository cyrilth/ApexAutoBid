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
/// Unit tests for <see cref="AuctionCancelledConsumer"/>. The unit seam is the consumer
/// itself: <see cref="IItemRepository"/> and <see cref="ConsumeContext{T}"/> are both
/// substituted, mirroring <c>SearchAppServiceTests</c>' substitute-the-dependency style —
/// no real MongoDB/RabbitMQ involved (that coverage belongs to the Testcontainers-backed
/// integration tests).
/// </summary>
public class AuctionCancelledConsumerTests
{
    private readonly IItemRepository _repository = Substitute.For<IItemRepository>();

    private AuctionCancelledConsumer BuildSut() =>
        new(_repository, NullLogger<AuctionCancelledConsumer>.Instance);

    private static ConsumeContext<AuctionCancelled> BuildContext(
        AuctionCancelled message, CancellationToken cancellationToken)
    {
        var context = Substitute.For<ConsumeContext<AuctionCancelled>>();
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
        CurrentHighBid = null
    };

    [Fact]
    public async Task Consume_WhenItemIsLive_SetsStatusToCancelledAndUpserts()
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new AuctionCancelled(id.ToString(), "bob"), CancellationToken.None));

        Assert.Equal("Cancelled", item.Status);
        await _repository.Received(1).UpsertAsync(
            Arg.Is<Item>(i => i.Id == id && i.Status == "Cancelled"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Finished")]
    [InlineData("ReserveNotMet")]
    [InlineData("Cancelled")]
    public async Task Consume_WhenItemAlreadyFinalized_IsIdempotentAndDoesNotUpsert(string existingStatus)
    {
        var id = Guid.NewGuid();
        var item = LiveItem(id);
        item.Status = existingStatus;
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var sut = BuildSut();

        await sut.Consume(BuildContext(new AuctionCancelled(id.ToString(), "bob"), CancellationToken.None));

        Assert.Equal(existingStatus, item.Status);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenItemNotFound_LogsAndReturnsWithoutThrowingOrRetrying()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Item?)null);
        var sut = BuildSut();

        // Must complete without throwing — this consumer deliberately does NOT retry a
        // missing item (see its XML remarks), unlike most of this service's other consumers.
        await sut.Consume(BuildContext(new AuctionCancelled(id.ToString(), "bob"), CancellationToken.None));

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenAuctionIdUnparsable_SkipsWithoutTouchingTheRepository()
    {
        var sut = BuildSut();

        await sut.Consume(BuildContext(new AuctionCancelled("not-a-guid", "bob"), CancellationToken.None));

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

        await sut.Consume(BuildContext(new AuctionCancelled(id.ToString(), "bob"), cts.Token));

        await _repository.Received().GetByIdAsync(id, cts.Token);
        await _repository.Received().UpsertAsync(Arg.Any<Item>(), cts.Token);
    }
}
