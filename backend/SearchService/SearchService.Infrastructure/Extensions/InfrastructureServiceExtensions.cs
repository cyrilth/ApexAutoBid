using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SearchService.Application.Services;
using SearchService.Domain.Interfaces;
using SearchService.Infrastructure.Data;
using SearchService.Infrastructure.Http;

namespace SearchService.Infrastructure.Extensions;

/// <summary>
/// Registers all Infrastructure-layer services into the DI container.
/// Call <c>builder.Services.AddInfrastructureServices(builder.Configuration)</c>
/// from the API's <c>Program.cs</c>.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MongoDbContext is the singleton holder DbInitializer.InitDbAsync populates with
        // the connected DB instance (MongoDB.Entities 25.1.0 has no static default-instance
        // gateway — see MongoDbContext's XML doc). Registered here so the same instance
        // DbInitializer resolves via GetRequiredService<MongoDbContext>() is the one
        // ItemRepository receives.
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IItemRepository, ItemRepository>();

        // ── Phase 2 Task 7 — Mongo outbox/inbox: DI-resolvable driver types ──────────
        //
        // MassTransit's Mongo outbox (wired in Program.cs's AddMassTransit) needs an
        // IMongoClient/IMongoDatabase it can resolve from the container — it has no
        // knowledge of MongoDB.Entities' own internally-held connection (see MongoDbContext's
        // XML doc). This client is therefore intentionally SEPARATE from the one
        // DbInitializer.InitDbAsync creates via DB.InitAsync, even though both point at the
        // exact same server and the same "search" database (DbInitializer.DatabaseName) —
        // two independent driver-level connections to one logical database, not a shared
        // instance. IMongoClient is a singleton because the driver's client already pools and
        // manages connections internally; a new one per request would be wasteful.
        services.AddSingleton<IMongoClient>(_ =>
        {
            var connectionString = configuration.GetConnectionString("MongoDbConnection")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:MongoDbConnection is not configured");
            return new MongoClient(connectionString);
        });

        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(DbInitializer.DatabaseName));

        // Phase 2 Task 6 — HTTP polling fallback. AuctionServiceUrl is required at startup
        // (no localhost fallback here — unlike RabbitMq:Host — since a silently-wrong default
        // would make sync failures look like "everything's fine, zero new auctions" instead
        // of a clear configuration error).
        services.AddHttpClient<IAuctionServiceClient, AuctionServiceHttpClient>((sp, http) =>
            {
                var baseUrl = configuration["AuctionServiceUrl"]
                    ?? throw new InvalidOperationException("AuctionServiceUrl is not configured");

                // HttpClient.BaseAddress + a relative request URI only combine correctly per
                // RFC 3986 relative resolution when the base ends with '/' AND the relative
                // URI has NO leading '/' (AuctionServiceHttpClient's requestUri values are
                // "api/auctions[?date=...]" — no leading slash, by design, to pair with this).
                // Without normalizing here, a base with no path (today's
                // "http://localhost:5054") happens to work either way, but a future base with
                // a sub-path (e.g. "http://gateway/auction", no trailing slash) would silently
                // drop "/auction" the moment a relative URI is resolved against it.
                http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            })
            // Standard resilience handler: retry (exponential backoff, a handful of attempts),
            // a circuit breaker that trips after sustained failures, and both per-attempt and
            // overall-request timeouts — Requirements §3.2's "Microsoft.Extensions.Http.
            // Resilience (Polly v8)". Defaults are intentionally not hand-tuned here: this is
            // a once-at-startup call, not a hot path, so the library's own sane defaults are
            // sufficient rather than a bespoke pipeline.
            .AddStandardResilienceHandler();

        return services;
    }
}
