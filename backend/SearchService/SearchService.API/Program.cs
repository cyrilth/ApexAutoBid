using MassTransit;
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

await DbInitializer.InitDbAsync(app.Services);

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
        await scope.ServiceProvider.GetRequiredService<IDataSyncService>().SyncAsync(CancellationToken.None);
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
