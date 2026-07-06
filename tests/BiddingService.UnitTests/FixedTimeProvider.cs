namespace BiddingService.UnitTests;

/// <summary>
/// Hand-rolled fixed-clock <see cref="TimeProvider"/> for deterministic "now"-dependent tests
/// (Phase 5 Task 15) — no FakeTimeProvider package needed for a single override. Mirrors
/// SearchService.UnitTests' identical helper.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
