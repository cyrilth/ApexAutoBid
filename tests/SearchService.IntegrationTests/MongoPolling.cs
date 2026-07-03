using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.IntegrationTests;

/// <summary>
/// Polling helpers for asserting eventual Mongo state after publishing an event onto the
/// real broker: the app's consumers (Phase 2 Task 4) process asynchronously off a real
/// RabbitMQ container, so a test can't assert on Mongo state immediately after
/// <c>IBus.Publish</c> returns — publishing only guarantees the broker accepted the message,
/// not that a consumer has processed it yet.
/// </summary>
internal static class MongoPolling
{
    // 20s: generous relative to normal consume latency (typically well under a second), but
    // still leaves real margin under the consumer endpoints' own worst-case in-process retry
    // window (Program.cs: UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(5))) — up
    // to 5 attempts, 5s apart, ~25s worst case — in case a message ever needs a retry.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Polls <see cref="IItemRepository.GetByIdAsync"/> until an item with <paramref name="id"/>
    /// exists (and, if supplied, satisfies <paramref name="predicate"/>), or fails the test
    /// with a clear message once <see cref="Timeout"/> elapses.
    /// </summary>
    public static async Task<Item> WaitForItemAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken cancellationToken,
        Func<Item, bool>? predicate = null,
        string? because = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IItemRepository>();
            var item = await repository.GetByIdAsync(id, cancellationToken);
            if (item is not null && (predicate is null || predicate(item)))
                return item;

            await Task.Delay(Interval, cancellationToken);
        }

        Assert.Fail(
            $"Item {id} was not observed in the expected state within {Timeout}" +
            (because is null ? "." : $" ({because})."));
        throw new UnreachableException(); // Assert.Fail always throws; unreachable in practice.
    }

    /// <summary>
    /// Polls <see cref="IItemRepository.GetByIdAsync"/> until it returns <see langword="null"/>
    /// (item removed), or fails the test with a clear message once <see cref="Timeout"/>
    /// elapses.
    /// </summary>
    public static async Task WaitForItemAbsentAsync(
        IServiceProvider services, Guid id, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IItemRepository>();
            var item = await repository.GetByIdAsync(id, cancellationToken);
            if (item is null)
                return;

            await Task.Delay(Interval, cancellationToken);
        }

        Assert.Fail($"Item {id} was still present {Timeout} after AuctionDeleted was published.");
    }
}
