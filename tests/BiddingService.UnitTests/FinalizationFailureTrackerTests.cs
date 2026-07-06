using BiddingService.Application.Services;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="FinalizationFailureTracker"/> (phase-end code review Warning 4).
/// </summary>
public class FinalizationFailureTrackerTests
{
    [Fact]
    public void RecordFailure_CalledRepeatedlyForTheSameAuction_ReturnsAnIncrementingCount()
    {
        var tracker = new FinalizationFailureTracker();
        var auctionId = Guid.NewGuid();

        Assert.Equal(1, tracker.RecordFailure(auctionId));
        Assert.Equal(2, tracker.RecordFailure(auctionId));
        Assert.Equal(3, tracker.RecordFailure(auctionId));
    }

    [Fact]
    public void RecordFailure_ForDifferentAuctions_TracksEachIndependently()
    {
        var tracker = new FinalizationFailureTracker();
        var auctionA = Guid.NewGuid();
        var auctionB = Guid.NewGuid();

        Assert.Equal(1, tracker.RecordFailure(auctionA));
        Assert.Equal(1, tracker.RecordFailure(auctionB));
        Assert.Equal(2, tracker.RecordFailure(auctionA));
    }

    [Fact]
    public void RecordSuccess_ResetsTheConsecutiveCountForThatAuction()
    {
        var tracker = new FinalizationFailureTracker();
        var auctionId = Guid.NewGuid();
        tracker.RecordFailure(auctionId);
        tracker.RecordFailure(auctionId);

        tracker.RecordSuccess(auctionId);

        Assert.Equal(1, tracker.RecordFailure(auctionId));
    }

    [Fact]
    public void RecordSuccess_ForAnAuctionNeverRecordedAsFailing_IsANoOp()
    {
        var tracker = new FinalizationFailureTracker();

        var exception = Record.Exception(() => tracker.RecordSuccess(Guid.NewGuid()));

        Assert.Null(exception);
    }
}
