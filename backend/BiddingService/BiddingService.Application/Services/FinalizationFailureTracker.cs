using System.Collections.Concurrent;

namespace BiddingService.Application.Services;

/// <summary>
/// Default <see cref="IFinalizationFailureTracker"/> implementation — a thread-safe in-memory
/// dictionary, since concurrent ticks never overlap (<c>AuctionFinalizerHostedService</c>'s own
/// remarks) but auctions within a single tick's batch are processed one after another on
/// whichever thread the timer callback runs on; <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// costs nothing extra here and rules out ever having to reason about it.
/// </summary>
public sealed class FinalizationFailureTracker : IFinalizationFailureTracker
{
    private readonly ConcurrentDictionary<Guid, int> _consecutiveFailures = new();

    public int RecordFailure(Guid auctionId) =>
        _consecutiveFailures.AddOrUpdate(auctionId, addValue: 1, updateValueFactory: (_, count) => count + 1);

    public void RecordSuccess(Guid auctionId) => _consecutiveFailures.TryRemove(auctionId, out _);
}
