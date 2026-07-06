using System.Diagnostics;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Polling helpers for asserting eventual Mongo state after publishing an event onto the real
/// broker: the app's consumer(s) process asynchronously off a real RabbitMQ container, so a
/// test can't assert on Mongo state immediately after <c>IBus.Publish</c> returns. Mirrors
/// <c>SearchService.IntegrationTests.MongoPolling</c>'s identical rationale and shape, adapted
/// to this service's own <see cref="IAuctionRepository"/>.
/// </summary>
internal static class RepositoryPolling
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Polls <see cref="IAuctionRepository.GetByIdAsync"/> until a local auction record with
    /// <paramref name="id"/> exists (and, if supplied, satisfies <paramref name="predicate"/>),
    /// or fails the test with a clear message once <see cref="Timeout"/> elapses.
    /// </summary>
    public static async Task<Auction> WaitForAuctionAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken cancellationToken,
        Func<Auction, bool>? predicate = null,
        string? because = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAuctionRepository>();
            var auction = await repository.GetByIdAsync(id, cancellationToken);
            if (auction is not null && (predicate is null || predicate(auction)))
                return auction;

            await Task.Delay(Interval, cancellationToken);
        }

        Assert.Fail(
            $"Local auction {id} was not observed in the expected state within {Timeout}" +
            (because is null ? "." : $" ({because})."));
        throw new UnreachableException(); // Assert.Fail always throws; unreachable in practice.
    }
}
