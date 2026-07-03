namespace SearchService.UnitTests;

/// <summary>
/// Hand-rolled fixed-clock <see cref="TimeProvider"/> for deterministic "now"-dependent
/// tests (Phase 2 Task 9) — no FakeTimeProvider package needed for a single override.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
