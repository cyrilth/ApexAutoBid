using System.Collections.Concurrent;
using System.Diagnostics;
using MassTransit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// A minimal, self-contained MassTransit "test harness" for asserting that the app under test
/// actually PUBLISHED a message onto the real RabbitMQ broker — the counterpart to
/// <c>MongoPolling</c>-style helpers (SearchService.IntegrationTests) that assert eventual Mongo
/// state for services that only ever consume.
/// </summary>
/// <remarks>
/// <para>
/// The app's own MassTransit bus (Program.cs's <c>AddMassTransit</c>) cannot be reused for this:
/// MassTransit throws if <c>AddMassTransit</c> is called twice in the same container, and the
/// existing sibling suites (SearchService/AuctionService) never needed to observe a PUBLISH
/// since neither of those services' integration tests assert on messages their own app
/// produces. Instead, this starts a second, completely independent bus — plain
/// <c>Bus.Factory.CreateUsingRabbitMq</c>, no DI container involved — pointed at the SAME
/// RabbitMQ container/vhost the app under test uses, with its own receive endpoint bound to
/// <typeparamref name="TMessage"/>'s exchange. MassTransit's default RabbitMQ topology binds any
/// receive endpoint declaring a handler for a message type to that type's publish exchange
/// regardless of which process/bus actually publishes to it — exactly the same mechanism that
/// lets, e.g., SearchService's real <c>AuctionCreatedConsumer</c> receive events AuctionService
/// publishes; here the test itself plays that "other service" role.
/// </para>
/// <para>
/// Each instance uses a randomly-named queue so back-to-back tests in the same shared,
/// long-lived <see cref="BiddingServiceApiCollection"/> container never observe each other's
/// messages.
/// </para>
/// </remarks>
internal sealed class RabbitMqPublishHarness<TMessage> : IAsyncDisposable
    where TMessage : class
{
    // 30s: generous relative to normal bus-outbox delivery latency (typically well under a
    // second once a Mongo transaction commits), but still leaves margin under the outbox
    // delivery worker's own QueryDelay (Program.cs: 10s) plus the consumer-side in-process
    // retry window (UseMessageRetry: up to 5 attempts, 5s apart) in case a message ever needs
    // a retry — mirrors SearchService.IntegrationTests.MongoPolling's identical reasoning.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly IBusControl _bus;
    private readonly ConcurrentQueue<TMessage> _received;

    private RabbitMqPublishHarness(IBusControl bus, ConcurrentQueue<TMessage> received)
    {
        _bus = bus;
        _received = received;
    }

    public static async Task<RabbitMqPublishHarness<TMessage>> StartAsync(
        string host, ushort port, string username, string password, CancellationToken cancellationToken)
    {
        var received = new ConcurrentQueue<TMessage>();

        var bus = global::MassTransit.Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(host, port, "/", h =>
            {
                h.Username(username);
                h.Password(password);
            });

            cfg.ReceiveEndpoint(
                $"bidding-test-harness-{typeof(TMessage).Name.ToLowerInvariant()}-{Guid.NewGuid():N}",
                e =>
                {
                    e.Handler<TMessage>(context =>
                    {
                        received.Enqueue(context.Message);
                        return Task.CompletedTask;
                    });
                });
        });

        await bus.StartAsync(cancellationToken);
        return new RabbitMqPublishHarness<TMessage>(bus, received);
    }

    /// <summary>
    /// Polls until a received message satisfies <paramref name="predicate"/> (or any message,
    /// if omitted), or fails the test with a clear message once <paramref name="timeout"/>
    /// (default 20s) elapses.
    /// </summary>
    public async Task<TMessage> WaitForMessageAsync(
        Func<TMessage, bool>? predicate,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + effectiveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var message in _received)
            {
                if (predicate is null || predicate(message))
                    return message;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        Assert.Fail($"No matching {typeof(TMessage).Name} message was observed within {effectiveTimeout}.");
        throw new UnreachableException(); // Assert.Fail always throws; unreachable in practice.
    }

    /// <summary>
    /// Waits briefly (well short of <see cref="DefaultTimeout"/>) and returns how many received
    /// messages satisfy <paramref name="predicate"/> — used to assert idempotency (e.g. "still
    /// exactly one AuctionFinished, even after a second finalizer tick") rather than presence.
    /// </summary>
    public async Task<int> CountAfterQuietPeriodAsync(
        Func<TMessage, bool> predicate, CancellationToken cancellationToken, TimeSpan? quietPeriod = null)
    {
        await Task.Delay(quietPeriod ?? TimeSpan.FromSeconds(3), cancellationToken);
        return _received.Count(predicate);
    }

    public async ValueTask DisposeAsync()
    {
        await _bus.StopAsync();
    }
}
