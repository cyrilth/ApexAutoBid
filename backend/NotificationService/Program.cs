using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Handlers;
using NotificationService.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── SignalR (Phase 6 Task 3) ──────────────────────────────────────────────────
//
// AddSignalR registers the hub infrastructure; NotificationHub itself is mapped to
// "/notifications" below. AddSingleton<IUserIdProvider, UsernameUserIdProvider> replaces the
// framework's default ClaimTypes.NameIdentifier-based user id with the "username" claim (Task
// 3.2) — see UsernameUserIdProvider's own remarks for why that matches AuctionFinished's
// Winner/Seller fields one-for-one.

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, UsernameUserIdProvider>();

// ── Authentication (Phase 6 Task 3.2) ─────────────────────────────────────────
//
// Identical wiring to every other JwtBearer consumer in the platform (Architecture.md §5.5):
// Authority from config (dev: https://localhost:5001), ValidAudience is the platform-wide
// "apexautobid" ApiScope/ApiResource, NameClaimType "username", ValidTypes restricted to
// Duende's "at+jwt" access-token typ header.
//
// OnMessageReceived is the one addition beyond that shared pattern, and it is SignalR-specific:
// browsers cannot set a custom Authorization header on the WebSocket handshake, so the
// JavaScript client instead appends the token as an "access_token" query-string parameter
// (the standard ASP.NET Core SignalR pattern) when it negotiates/connects. The default
// JwtBearerHandler only ever looks at the Authorization header, so without this handler an
// authenticated SignalR connection would always be rejected. Scoped to paths starting with
// "/notifications" (StartsWithSegments, not an exact match) so it also covers SignalR's
// transport-specific sub-paths (e.g. "/notifications/negotiate") without affecting any other
// endpoint this service might expose in the future.
//
// Authentication here is deliberately OPTIONAL, not required (Task 3.1 — NotificationHub
// carries no [Authorize]): a token that fails to validate simply leaves the connection
// anonymous (Clients.All broadcasts only) rather than rejecting the handshake outright.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidAudience = "apexautobid";
        options.TokenValidationParameters.NameClaimType = "username";
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// ── Messaging (MassTransit + RabbitMQ — Phase 6 Task 2) ──────────────────────
//
// Consumer-only: this service has no database and publishes no events (Architecture.md's
// resilience table explicitly calls out Notification as "not applicable" for the
// MassTransit outbox), so there is deliberately no AddMongoDbOutbox/AddEntityFrameworkCoreOutbox
// call and no AddConfigureEndpointsCallback wiring a consumer-side inbox — unlike
// SearchService.API/BiddingService.API, which both have a database to enlist that inbox's
// state in. The three Task 4 consumers below (AuctionCreated/BidPlaced/AuctionFinished) push
// straight to IHubContext<NotificationHub>, which has no local state for redelivery to
// corrupt — see each consumer's own "idempotent by construction" remark — so no inbox-based
// dedup is needed for correctness here.
//
// KebabCaseEndpointNameFormatter with the "notification" prefix produces queue names like
// "notification-auction-created-consumer", mirroring SearchService's "search-*"/BiddingService's
// "bidding-*" convention on the same shared RabbitMQ broker.

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining<NotificationService.Consumers.AuctionCreatedConsumer>();

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notification", false));

    // Tags MassTransit's auto-registered bus health check "ready" so it is included in the
    // GET /health/ready predicate below (Phase 6 Task 8 / Requirements §13.4) — mirrors
    // SearchService.API's/BiddingService.API's identical wiring.
    x.ConfigureHealthCheckOptions(options => options.Tags.Add("ready"));

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

// ── Health checks (Phase 6 Task 8) ────────────────────────────────────────────
//
// No database to check here (see the messaging comment above) — the MassTransit/RabbitMQ bus
// health check registered via ConfigureHealthCheckOptions above is the only "ready"-tagged
// check this service has, added to the registry by MassTransit itself once AddMassTransit
// runs; this call just ensures the health-checks middleware/endpoints are available at all.

builder.Services.AddHealthChecks();

// ── Global error handling ──────────────────────────────────────────────────────
//
// Same wiring as every other service (Requirements §13.1): AddProblemDetails stamps a traceId
// extension on every ProblemDetails this service writes, and GlobalExceptionHandler converts
// genuinely unhandled exceptions into a 500 ProblemDetails (full exception in Development,
// generic message in Production). This service has no REST API of its own (Architecture.md
// §10 excludes it from OpenAPI/Scalar) — this backstop covers the SignalR negotiate endpoint
// and any exception thrown before a hub method/consumer completes.

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

// UseExceptionHandler is registered first so it wraps the entire remaining pipeline — any
// unhandled exception from authentication or the hub/health endpoints below is caught by
// GlobalExceptionHandler and returned as a ProblemDetails 500.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

// ── SignalR hub (Phase 6 Task 3) ──────────────────────────────────────────────
//
// No .RequireAuthorization() call here — NotificationHub itself carries no [Authorize]
// attribute (Task 3.1), so anonymous connections are accepted and receive Clients.All
// broadcasts; an authenticated connection (JWT via the access_token query string, above) is
// additionally reachable by username through UsernameUserIdProvider for the targeted
// AuctionWon/AuctionSellerResult messages (Task 4.4).

app.MapHub<NotificationHub>("/notifications");

// ── Health endpoints (Phase 6 Task 8) ─────────────────────────────────────────
//
// Anonymous per Requirements §13.4. /health/live never runs a check (Predicate = _ => false)
// so it reflects only "is the process up". /health/ready only runs checks tagged "ready" (the
// MassTransit/RabbitMQ bus check registered above — no database check, this service has none).
// Mirrors SearchService.API's/BiddingService.API's identical Task 8/14/22 wiring.

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test project
// so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
