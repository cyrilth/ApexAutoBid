using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;
using Xunit;

namespace SearchService.IntegrationTests;

/// <summary>
/// Boots the real Search Service in-memory against throwaway MongoDB and RabbitMQ
/// containers. Mirrors AuctionService.IntegrationTests' CustomWebAppFactory pattern:
/// the app's MassTransit/RabbitMQ config is left untouched (MassTransit throws if
/// AddMassTransit is called twice), so a real broker is provided; the RabbitMQ container
/// gets a non-guest user because RabbitMQ's built-in guest user is loopback-restricted.
/// </summary>
/// <remarks>
/// <b>Mongo MUST be a single-node replica set:</b> the app's MassTransit Mongo inbox
/// (UseMongoDbOutbox on all five receive endpoints — Phase 2 Task 7) starts a Mongo
/// transaction on every consume. A standalone Mongo container fails with "Standalone
/// servers do not support transactions" the moment a consumer actually runs (not at
/// connect/startup time) — <c>Testcontainers.MongoDb</c>'s <c>WithReplicaSet(...)</c>
/// starts the container as a single-node replica set instead.
/// </remarks>
/// <remarks>
/// <b>Connection string just works, unmodified — here's why:</b> no <c>WithUsername</c>/
/// <c>WithPassword</c> are set below, but auth is NOT disabled — <c>MongoDbBuilder.Init()</c>
/// applies default <c>mongo</c>/<c>mongo</c> credentials when none are supplied, and
/// <c>MongoDbContainer.GetConnectionString()</c> already hardcodes <c>directConnection=true</c>
/// in the string it returns. That combination is exactly why this container's connection
/// string works as-is for both MongoDB.Entities' init and the MassTransit outbox client,
/// with no manual credential or <c>directConnection</c> handling needed here (unlike the dev
/// <c>docker-compose.infra.yml</c> setup, which needs both handled explicitly).
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
                ["RabbitMq:Host"] = _rabbitMq.Hostname,
                ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
                ["RabbitMq:Username"] = "apex",
                ["RabbitMq:Password"] = "apex",
                // Dead-but-fast: port 1 refuses the connection immediately (no DNS lookup,
                // no listener), so the Task 6 startup sync's resilience pipeline burns through
                // its retries quickly and hits the documented graceful-failure path rather
                // than timing out against a genuinely unreachable host.
                ["AuctionServiceUrl"] = "http://localhost:1",
            });
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
/// Groups every test class that depends on <see cref="CustomWebAppFactory"/> into a single
/// xUnit collection so they share one factory instance instead of each spinning up their own
/// MongoDB/RabbitMQ containers. Collection members run sequentially against the shared
/// instance — tests use unique auction Guids per test to avoid interfering with each other.
/// </summary>
[CollectionDefinition(Name)]
public class SearchServiceApiCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "SearchService.API";
}
