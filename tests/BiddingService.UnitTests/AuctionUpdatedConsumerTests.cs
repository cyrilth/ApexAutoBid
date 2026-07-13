using BiddingService.Application.Consumers;
using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AuctionUpdatedConsumer"/> (Phase 11 Task 5.3) — applying
/// <c>AuctionUpdated.AuctionEnd</c> to the local auction projection when present, and doing
/// nothing when it's absent. Mirrors <c>AuctionCancelledConsumerTests</c>' identical "substitute
/// <see cref="ConsumeContext{T}"/> directly" convention.
/// </summary>
public class AuctionUpdatedConsumerTests
{
    private static ConsumeContext<AuctionUpdated> BuildContext(AuctionUpdated message)
    {
        var context = Substitute.For<ConsumeContext<AuctionUpdated>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static AuctionUpdated SampleMessage(string id, DateTime? auctionEnd) => new(
        Id: id,
        Make: "Ford",
        Model: "GT",
        Color: "Blue",
        Mileage: 1000,
        Year: 2020,
        ImageUrl: "https://example.test/image.jpg",
        ThumbnailUrl: null,
        AuctionEnd: auctionEnd);

    // ── 5.3 — a non-null AuctionEnd is applied to the local record (admin "end now") ────────

    [Fact]
    public async Task Consume_WhenAuctionEndIsPresent_UpdatesTheLocalAuctionEnd()
    {
        var auctionId = Guid.NewGuid();
        var newEnd = new DateTime(2026, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IAuctionRepository>();
        var consumer = new AuctionUpdatedConsumer(repository, NullLogger<AuctionUpdatedConsumer>.Instance);

        await consumer.Consume(BuildContext(SampleMessage(auctionId.ToString(), newEnd)));

        await repository.Received(1).UpdateAuctionEndAsync(auctionId, newEnd, Arg.Any<CancellationToken>());
    }

    // ── A null AuctionEnd (an update that didn't touch it) is a genuine no-op ────────────────

    [Fact]
    public async Task Consume_WhenAuctionEndIsAbsent_NeverTouchesTheRepository()
    {
        var auctionId = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        var consumer = new AuctionUpdatedConsumer(repository, NullLogger<AuctionUpdatedConsumer>.Instance);

        await consumer.Consume(BuildContext(SampleMessage(auctionId.ToString(), auctionEnd: null)));

        await repository.DidNotReceive().UpdateAuctionEndAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
