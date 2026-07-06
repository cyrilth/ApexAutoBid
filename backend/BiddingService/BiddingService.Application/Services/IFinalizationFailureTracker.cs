namespace BiddingService.Application.Services;

/// <summary>
/// In-memory, per-process consecutive-failure counter for the background auction finalizer
/// (phase-end code review Warning 4) — makes an auction that fails to finalize on EVERY tick
/// (rather than an isolated transient blip) operationally visible, without pulling in any
/// metrics infrastructure (none exists yet in this codebase — kept deliberately simple).
/// </summary>
/// <remarks>
/// Registered as a singleton (<c>ApplicationServiceExtensions</c>): the failure count must
/// survive across ticks, but <see cref="AuctionFinalizationAppService"/> itself is scoped — a
/// fresh instance is resolved every tick by <c>AuctionFinalizerHostedService</c>'s own
/// per-tick <c>IServiceScope</c> (see that class's remarks), so an instance field on the
/// scoped service itself could never accumulate a "consecutive" count across ticks. A
/// singleton tracker is the smallest state that can. Per-process only — a second replica would
/// track independently; acceptable for a first cut per the "keep it simple" instruction, same
/// as every other in-memory-only concern in this service today (e.g. no distributed lock
/// anywhere in this codebase).
/// </remarks>
public interface IFinalizationFailureTracker
{
    /// <summary>
    /// Records a finalization failure for <paramref name="auctionId"/> and returns the new
    /// consecutive-failure count (starting at 1 for the first recorded failure since the last
    /// success, or since process start).
    /// </summary>
    int RecordFailure(Guid auctionId);

    /// <summary>
    /// Clears any tracked consecutive-failure count for <paramref name="auctionId"/> — call
    /// after a successful (or successfully-skipped, e.g. already-finalized-by-another-pass)
    /// finalization attempt.
    /// </summary>
    void RecordSuccess(Guid auctionId);
}
