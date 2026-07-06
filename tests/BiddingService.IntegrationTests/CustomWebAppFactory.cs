using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Boots the real Bidding Service in-memory against throwaway MongoDB and RabbitMQ containers.
/// Mirrors <c>SearchService.IntegrationTests.CustomWebAppFactory</c>'s pattern (same "Mongo MUST
/// be a single-node replica set" and "connection string just works unmodified" rationale — the
/// app's MassTransit Mongo bus-outbox, wired identically here, starts a Mongo transaction on
/// every bid placement/finalization). Authentication is replaced with <see cref="TestAuthHandler"/>,
/// mirroring <c>AuctionService.IntegrationTests</c>' identical convention.
/// </summary>
/// <remarks>
/// <b>The background finalizer's interval is deliberately long by default (one hour):</b> the
/// real <c>AuctionFinalizerHostedService</c> is left running (it can't be swapped out without
/// touching Program.cs), so the shared, long-lived <see cref="BiddingServiceApiCollection"/>
/// instance every non-finalizer test runs against must never have it tick mid-test and finalize
/// something a test didn't intend it to. <see cref="ShortIntervalWebAppFactory"/> is the only
/// place this is ever shortened, in its own dedicated collection/containers, and is used by
/// exactly one coarse hosted-service smoke test — see
/// <c>AuctionFinalizerHostedServiceTests</c>'s remarks for why the rest of Task 16.4's
/// assertions instead invoke <c>IAuctionFinalizationService</c> directly.
/// </remarks>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7")
        .WithReplicaSet("rs0")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("apex")
        .WithPassword("apex")
        .Build();

    private readonly int _finalizationIntervalSeconds;
    private readonly int _finalizationGraceSeconds;

    public CustomWebAppFactory() : this(finalizationIntervalSeconds: 3600, finalizationGraceSeconds: 0)
    {
    }

    /// <param name="finalizationIntervalSeconds">See the class remarks.</param>
    /// <param name="finalizationGraceSeconds">
    /// Phase-end code review Critical 2's grace period — deliberately 0 by default here (NOT
    /// the real service's own 10s default), matching this task's explicit instruction to keep
    /// this suite's existing "an auction 5 minutes past AuctionEnd is immediately eligible"
    /// tests fast and meaningful. <see cref="GracePeriodWebAppFactory"/> is the only place a
    /// non-zero grace is ever configured, in its own dedicated collection, specifically to
    /// assert the grace period itself is respected.
    /// </param>
    protected CustomWebAppFactory(int finalizationIntervalSeconds, int finalizationGraceSeconds = 0)
    {
        _finalizationIntervalSeconds = finalizationIntervalSeconds;
        _finalizationGraceSeconds = finalizationGraceSeconds;
    }

    /// <summary>RabbitMQ connection details for tests that need to attach their own bus (see
    /// <see cref="RabbitMqPublishHarness{TMessage}"/>) to observe what the app under test
    /// publishes, without touching the app's own already-configured MassTransit bus.</summary>
    public string RabbitMqHost => _rabbitMq.Hostname;

    public ushort RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public const string RabbitMqUsername = "apex";
    public const string RabbitMqPassword = "apex";

    public async ValueTask InitializeAsync()
    {
        await _mongo.StartAsync();
        await _rabbitMq.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDbConnection"] = _mongo.GetConnectionString(),
                ["RabbitMq:Host"] = RabbitMqHost,
                ["RabbitMq:Port"] = RabbitMqPort.ToString(),
                ["RabbitMq:Username"] = RabbitMqUsername,
                ["RabbitMq:Password"] = RabbitMqPassword,
                ["IdentityServiceUrl"] = "https://localhost:5001",
                // Dead-but-fast: port 1 refuses the connection immediately (no DNS lookup, no
                // listener). No test in this suite ever bids on an auction absent from this
                // service's own local Mongo projection, so GrpcFallbackAuctionProvider's fallback
                // path is never actually exercised here — Task 15.6's unit tests already cover
                // it directly. This just needs to be configured (Program.cs throws at startup
                // otherwise), never successfully dialled.
                ["Grpc:AuctionServiceUrl"] = "http://localhost:1",
                ["Bidding:FinalizationIntervalSeconds"] = _finalizationIntervalSeconds.ToString(),
                ["Bidding:FinalizationGraceSeconds"] = _finalizationGraceSeconds.ToString(),
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Make the header-driven test scheme the default so [Authorize] uses it.
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // xUnit v3's IAsyncLifetime inherits IAsyncDisposable, so teardown is a ValueTask-returning
    // DisposeAsync — the same signature WebApplicationFactory already exposes.
    public override async ValueTask DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _mongo.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Groups every test class that depends on the shared, long-finalization-interval
/// <see cref="CustomWebAppFactory"/> into a single xUnit collection so they share one factory
/// instance instead of each spinning up their own MongoDB/RabbitMQ containers. Collection
/// members run sequentially against the shared instance — tests use unique auction/bidder
/// values per test to avoid interfering with each other and with the service's own seeded data.
/// </summary>
[CollectionDefinition(Name)]
public class BiddingServiceApiCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "BiddingService.API";
}

/// <summary>
/// A <see cref="CustomWebAppFactory"/> with a short (2s) background-finalizer interval, in its
/// own dedicated containers/collection — deliberately NOT shared with
/// <see cref="BiddingServiceApiCollection"/>, so a real hosted-service tick can never race any
/// other test's Mongo state. Used by exactly one coarse smoke test (see
/// <c>AuctionFinalizerHostedServiceTests</c>).
/// </summary>
public sealed class ShortIntervalWebAppFactory() : CustomWebAppFactory(finalizationIntervalSeconds: 2, finalizationGraceSeconds: 0);

[CollectionDefinition(Name)]
public class BiddingServiceFinalizerCollection : ICollectionFixture<ShortIntervalWebAppFactory>
{
    public const string Name = "BiddingService.API.Finalizer";
}

/// <summary>
/// A <see cref="CustomWebAppFactory"/> with a non-zero (30s) finalization grace period
/// (phase-end code review Critical 2), in its own dedicated containers/collection — the ONLY
/// place this suite ever configures a non-zero grace, specifically so
/// <c>AuctionFinalizationTests</c>' grace-period assertions can rely on it without affecting
/// (or being affected by) every other test's "immediately eligible" assumption. The
/// finalization INTERVAL stays long (one hour, the base default) since these tests invoke
/// <c>IAuctionFinalizationService</c> directly (mirrors <see cref="CustomWebAppFactory"/>'s own
/// remarks on why direct invocation is used instead of waiting on the real timer).
/// </summary>
public sealed class GracePeriodWebAppFactory() : CustomWebAppFactory(finalizationIntervalSeconds: 3600, finalizationGraceSeconds: 30);

[CollectionDefinition(Name)]
public class BiddingServiceGracePeriodCollection : ICollectionFixture<GracePeriodWebAppFactory>
{
    public const string Name = "BiddingService.API.GracePeriod";
}
