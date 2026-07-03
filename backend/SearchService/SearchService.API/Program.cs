using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Scalar.AspNetCore;
using SearchService.API.Handlers;
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

    // Tags MassTransit's auto-registered bus health check "ready" so it is included in the
    // GET /health/ready predicate below (Phase 2 Task 14 / Requirements §13.4) — it reports
    // unhealthy whenever the RabbitMQ broker connection is down, giving us RabbitMQ readiness
    // for free without a second, redundant broker connection. Mirrors AuctionService.API's
    // identical Task 21 wiring.
    x.ConfigureHealthCheckOptions(options => options.Tags.Add("ready"));

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

// ── Health checks (Phase 2 Task 14) ───────────────────────────────────────────
//
// GET /health/live reports 200 as soon as the process is up (no checks — mapped with
// Predicate = _ => false below). GET /health/ready fans out to the "ready"-tagged checks:
// MongoDB via AspNetCore.HealthChecks.MongoDb, and RabbitMQ via MassTransit's own bus health
// check (tagged "ready" above). Mirrors AuctionService.API's Task 21 pattern with MongoDB in
// place of PostgreSQL. The MongoDB check pings the same "search" IMongoDatabase singleton
// Task 7's outbox wiring already registers (see InfrastructureServiceExtensions) rather than
// opening a third driver-level connection — resolved lazily via the service provider (not
// eagerly at registration time), so it picks up whatever connection the integration test host
// overrides ConnectionStrings:MongoDbConnection to, the same way AuctionService's deferred
// NpgSql connection-string factory does.
//
// Explicit timeout: HealthCheckRegistration otherwise defaults Timeout to InfiniteTimeSpan
// (no framework-applied bound), and MongoDbHealthCheck internally retries its {ping:1} command
// up to twice — with Task 7's IMongoClient on the driver's default 30s ServerSelectionTimeout,
// a down Mongo would otherwise block /health/ready for ~60s worst case, far beyond the
// 1-5s timeoutSeconds Phase 9's Kubernetes probes will use. The timeout spans the whole check
// invocation (the framework's linked cancellation token cuts through both ping attempts, and
// cancellation is not retried), bounding that worst case to ~5s.
// (AuctionService's AddNpgSql shares the same omitted-timeout root cause but is single-attempt
// and so only ~15s-bounded; bringing it in line is tracked for a future cross-service
// consistency pass, deliberately not changed here.)

builder.Services.AddHealthChecks()
    .AddMongoDb(
        sp => sp.GetRequiredService<IMongoDatabase>(),
        name: "mongodb",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));

// ── Controllers ───────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// ── Global error handling (Phase 2 Task 13) ──────────────────────────────────
//
// AddProblemDetails() makes every error response (400 model-validation failures via
// [ApiController], SearchController's own handcrafted 400s, and 500s from
// GlobalExceptionHandler) RFC 7807 application/problem+json. CustomizeProblemDetails stamps a
// traceId extension on framework-generated ProblemDetails — the automatic [ApiController]
// model-validation 400s and the 500s GlobalExceptionHandler writes via IProblemDetailsService —
// correlating those responses back to the corresponding log entry (Requirements §13.1).
// SearchController's own hand-constructed 400s (BadRequest(new ProblemDetails{...})) are
// serialized directly by the normal output formatter and bypass IProblemDetailsService/
// ProblemDetailsFactory entirely, so they do NOT get a traceId — they carry a self-explanatory
// Title/Detail instead and have no corresponding log entry to correlate to, matching
// AuctionService's identical behavior. (Routing them through ProblemDetailsFactory too is a
// deliberate non-goal here — tracked as a future cross-service consistency pass.)
//
// GlobalExceptionHandler (IExceptionHandler) catches unhandled exceptions, always logs the
// full exception via ILogger, and returns a 500 ProblemDetails whose Detail is the full
// exception in Development and a generic message in Production. It mirrors
// AuctionService.API's GlobalExceptionHandler (Phase 1 Task 19) verbatim; SearchController's
// handcrafted 400s (invalid orderBy/filterBy/pageNumber/pageSize) are normal returned results,
// not exceptions, so they are unaffected by — and never reach — this handler.

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── OpenAPI + Scalar (Phase 2 Task 12) ────────────────────────────────────────
//
// Deliberately no security scheme / document or operation transformers here — unlike
// AuctionService.API's BearerSecuritySchemeTransformer + AuthorizeOperationTransformer pair,
// this service has no authentication middleware wired up at all (see SearchController's own
// comment): GET api/search is anonymous-only, so there is no [Authorize] endpoint for a
// security requirement to ever attach to and nothing to declare a Bearer scheme for.

builder.Services.AddOpenApi();

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

// UseExceptionHandler is registered first so it wraps the entire remaining pipeline — any
// unhandled exception from controllers or elsewhere downstream is caught by
// GlobalExceptionHandler and returned as a ProblemDetails 500 (Phase 2 Task 13). This service
// has no authentication middleware to sit ahead of (see the OpenAPI/Scalar comment below), so
// unlike AuctionService.API there is nothing between this call and MapControllers().
app.UseExceptionHandler();

app.MapControllers();

app.MapGet("/", () => Results.Ok("SearchService is running."));

// ── Health endpoints (Phase 2 Task 14) ────────────────────────────────────────
//
// Anonymous per Requirements §13.4. /health/live never runs a check (Predicate = _ => false)
// so it reflects only "is the process up". /health/ready only runs checks tagged "ready"
// (MongoDB + the MassTransit/RabbitMQ bus check registered above). Mirrors AuctionService.API's
// Task 21 wiring exactly — same route names, same predicates, same AllowAnonymous() call.

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

// ── OpenAPI document + Scalar UI (Phase 2 Task 12) ────────────────────────────
//
// Mapped unconditionally (not dev-only) — mirrors AuctionService.API's Task 16 decision
// (see Docs/Tasks.md DoD and Architecture.md §10). MapOpenApi() serves the raw document at
// /openapi/v1.json; MapScalarApiReference() serves the interactive Scalar UI at /scalar,
// pointed at that document. Both are anonymous by default here since this service has no
// authentication middleware to require in the first place.

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
});

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
