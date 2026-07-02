using MassTransit;
using MongoDB.Driver;
using SearchService.Application.Extensions;
using SearchService.Application.Services;
using SearchService.Infrastructure.Data;
using SearchService.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (Phase 2 Task 3: MongoDB connection setup) ───────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster) ───────────────────────────────────────────

builder.Services.AddApplicationServices();

// ── Messaging (MassTransit + RabbitMQ) ───────────────────────────────────────
//
// The five Phase 2 Task 4 consumers (AuctionCreated/Updated/Deleted, BidPlaced,
// AuctionFinished) keep the search index in sync with the Auction/Bidding Services.
// No EF/Mongo transactional outbox-inbox here yet — that lands with the Mongo change-
// stream/outbox work in Phase 2 Task 7.
//
// KebabCaseEndpointNameFormatter with the "search" prefix produces queue names like
// "search-auction-created", mirroring AuctionService.API's "auction-<consumer-name>"
// convention on the same shared RabbitMQ broker.
//
// UseMessageRetry gives every consumer endpoint a modest redelivery policy: AuctionService
// does not configure one today, so this is a deliberate addition for Search's consumers —
// transient failures (e.g. a momentarily unreachable MongoDB) get 5 attempts, 5 seconds
// apart, before the message is moved to its endpoint's _error queue.

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining<SearchService.Application.Consumers.AuctionCreatedConsumer>();

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("search", false));

    // ── Mongo outbox/inbox (Phase 2 Task 7) ──────────────────────────────────────
    //
    // Reality check — this service PUBLISHES nothing (it only consumes), so the OUTBOX side
    // of this pattern (store-and-forward of messages published from inside a consumer) is
    // dormant today. It's configured now anyway so it's ready the moment this service (or
    // the future BiddingService, built the same way) starts publishing, matching
    // Architecture's resilience table (Auction, Search, Bidding all use an outbox). The side
    // that's actually active immediately is the INBOX: UseMongoDbOutbox below wraps each
    // receive in a Mongo session and records inbox state keyed by MessageId, deduping
    // redelivery of the exact same message within DuplicateDetectionWindow.
    //
    // Honesty point (do not remove): ItemRepository's writes go through MongoDB.Entities'
    // own client/session (see MongoDbContext's XML doc) and do NOT enlist in this outbox's
    // Mongo transaction — an item write is therefore not atomic with the inbox-state write.
    // Consumer idempotency (Phase 2 Task 4 — e.g. AuctionCreatedConsumer's
    // upsert-keyed-on-Guid, BidPlacedConsumer's atomic conditional update) remains the
    // PRIMARY correctness guarantee; the inbox is a redelivery-dedup optimization layered on
    // top of that, not a replacement for it.
    x.AddMongoDbOutbox(o =>
    {
        o.ClientFactory(provider => provider.GetRequiredService<IMongoClient>());
        o.DatabaseFactory(provider => provider.GetRequiredService<IMongoDatabase>());

        // 30 minutes — MassTransit's own default, made explicit rather than left implicit.
        // A redelivery with the same MessageId older than this window is treated as new
        // (re-processed) rather than deduped; this service's consumers are independently
        // idempotent (Task 4) regardless, so a window miss is a performance concern, not a
        // correctness one.
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    // Applies UseMongoDbOutbox to every receive endpoint MassTransit auto-configures from the
    // discovered consumers below (all five Task 4 consumers), not just one. The retry policy
    // configured on cfg (UseMessageRetry) runs BEFORE the inbox commit — a message that fails
    // and retries internally still results in exactly one inbox entry once the receive
    // pipeline as a whole succeeds or exhausts retries; the inbox records the outcome of the
    // full pipeline, not each individual retry attempt.
    x.AddConfigureEndpointsCallback((context, name, cfg) => cfg.UseMongoDbOutbox(context));

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitPort = builder.Configuration.GetValue<ushort?>("RabbitMq:Port") ?? 5672;
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", rabbitPort, "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(5)));

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddControllers();

var app = builder.Build();

// app.Lifetime.ApplicationStopping is threaded through both startup calls below as
// defense-in-depth cancellation (Task 8 code review) — see DbInitializer.ConnectWithRetryAsync's
// XML remarks ("Cancellation") for the caveat that pre-app.Run() signal delivery is
// best-effort, since the generic host's console lifetime doesn't register its Ctrl+C/SIGTERM
// handlers until Run()/StartAsync() actually starts.
await DbInitializer.InitDbAsync(app.Services, app.Lifetime.ApplicationStopping);

// ── Phase 2 Task 6: HTTP polling fallback sync ────────────────────────────────
//
// Ordering invariant (do not move this below app.Run()): AddMassTransit above only
// registers MassTransit's hosted service — it doesn't open the RabbitMQ connection or start
// consuming until app.Run() starts every IHostedService. Running the sync here, between
// DbInitializer and app.Run(), guarantees it completes and establishes its baseline BEFORE
// the bus starts. That way, any events already queued in RabbitMQ (including ones published
// while this service was down) are applied by the event consumers ON TOP of the synced
// baseline once the bus starts, instead of a stale sync silently overwriting event-driven
// changes that landed first.
//
// Deliberately broad catch (Exception) here too: DataSyncService already contains failures
// internally at the HTTP level and per-item level (see its "Failure policy" XML doc), but
// this is the last line of defense at the top-level startup boundary — nothing thrown by the
// sync path, however unexpected, may be allowed to reach here and stop app.Run() from
// starting the host. Because this sync re-runs on every restart, an unhandled exception at
// this point would otherwise crash the service on every single startup attempt until
// whatever caused it is fixed out-of-band — exactly the scenario this whole failure policy
// exists to prevent.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<IDataSyncService>()
            .SyncAsync(app.Lifetime.ApplicationStopping);
    }
    catch (Exception ex)
    {
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Auction Service sync threw unexpectedly — continuing startup");
    }
}

app.MapControllers();

app.MapGet("/", () => Results.Ok("SearchService is running."));

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
