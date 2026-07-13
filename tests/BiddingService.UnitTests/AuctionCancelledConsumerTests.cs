using BiddingService.Application.Consumers;
using BiddingService.Domain.Interfaces;
using Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AuctionCancelledConsumer"/> (Phase 11 Task 5.2). The unit seam is
/// the consumer itself: <see cref="IAuctionRepository"/> is substituted, and
/// <see cref="ConsumeContext{T}"/> — a genuine MassTransit interface — is substituted directly
/// rather than hosted through a real bus, mirroring this suite's established "test the seam
/// directly" convention (<c>BidPlacementUnitOfWorkTests</c> et al.) for a consumer this simple.
/// </summary>
public class AuctionCancelledConsumerTests
{
    private static ConsumeContext<AuctionCancelled> BuildContext(AuctionCancelled message)
    {
        var context = Substitute.For<ConsumeContext<AuctionCancelled>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    [Fact]
    public async Task Consume_MarksTheLocalAuctionFinished()
    {
        var auctionId = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        var consumer = new AuctionCancelledConsumer(repository, NullLogger<AuctionCancelledConsumer>.Instance);
        var message = new AuctionCancelled(auctionId.ToString(), "bob");

        await consumer.Consume(BuildContext(message));

        await repository.Received(1).MarkFinishedAsync(auctionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_IsIdempotentAcrossRedelivery()
    {
        // Redelivery of the same event is a genuine no-op — MarkFinishedAsync unconditionally
        // (re-)sets Finished = true, never throws, and this consumer publishes nothing itself.
        var auctionId = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        var consumer = new AuctionCancelledConsumer(repository, NullLogger<AuctionCancelledConsumer>.Instance);
        var message = new AuctionCancelled(auctionId.ToString(), "bob");

        await consumer.Consume(BuildContext(message));
        await consumer.Consume(BuildContext(message));

        await repository.Received(2).MarkFinishedAsync(auctionId, Arg.Any<CancellationToken>());
    }
}
