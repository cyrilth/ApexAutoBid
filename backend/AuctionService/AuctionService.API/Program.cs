using AuctionService.API.Handlers;
using AuctionService.API.OpenApi;
using AuctionService.Application.Extensions;
using AuctionService.Infrastructure.Data;
using AuctionService.Infrastructure.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (DbContext + repositories) ───────────────────────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster + IAuctionService) ─────────────────────────

builder.Services.AddApplicationServices();

// ── Messaging (MassTransit + RabbitMQ + EF Core transactional Outbox) ────────
//
// The transactional Outbox is backed by AuctionDbContext so that published events
// and domain writes are committed atomically in the same PostgreSQL transaction.
// QueryDelay controls how frequently the outbox delivery worker polls for unsent
// messages. UseBusOutbox() hooks MassTransit's IPublishEndpoint / ISendEndpoint
// to write to the outbox rather than sending directly to the broker.
//
// KebabCaseEndpointNameFormatter with the "auction" prefix produces queue names
// like "auction-<consumer-name>", keeping queues identifiable and collision-free
// on the shared RabbitMQ broker.

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AuctionDbContext>(o =>
    {
        o.QueryDelay = TimeSpan.FromSeconds(10);
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // Discovers and registers BidPlacedConsumer and AuctionFinishedConsumer (and any
    // future consumer added to this namespace) without needing to list each one by hand.
    x.AddConsumersFromNamespaceContaining<AuctionService.Application.Consumers.BidPlacedConsumer>();

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("auction", false));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        // The EF Core transactional Inbox (UseEntityFrameworkOutbox, backed by the migrated
        // InboxState table) is intentionally NOT wired here. It could be applied to every
        // auto-configured endpoint via x.AddConfigureEndpointsCallback(...), but the consumers
        // are already idempotent by design — BidPlacedConsumer via an atomic conditional UPDATE,
        // AuctionFinishedConsumer via its Status != Live guard — so broker-level exactly-once
        // deduplication is not required for correctness. Left as a future enhancement.
        cfg.ConfigureEndpoints(context);
    });
});

// ── Controllers ───────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// ── Global error handling (Task 19) ──────────────────────────────────────────
//
// AddProblemDetails() makes every error response (400 model-validation failures via
// [ApiController], 404/403/etc. returned explicitly by controllers, and 500s from
// GlobalExceptionHandler) RFC 7807 application/problem+json. CustomizeProblemDetails
// stamps a traceId extension on every ProblemDetails the service writes, correlating
// the response back to the corresponding log entry (Requirements §13.1).
//
// GlobalExceptionHandler (IExceptionHandler) catches unhandled exceptions, always logs
// the full exception via ILogger, and returns a 500 ProblemDetails whose Detail is the
// full exception in Development and a generic message in Production.

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── OpenAPI + Scalar (Task 16) ────────────────────────────────────────────────
//
// BearerSecuritySchemeTransformer declares the "Bearer" HTTP security scheme (the
// generator does not infer it from [Authorize]) and stamps Info.Title/Version.
// AuthorizeOperationTransformer then attaches that scheme, as a requirement, only
// to operations whose endpoint actually requires authorization.

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<AuthorizeOperationTransformer>();
});

// ── Authentication / Authorization ───────────────────────────────────────────
//
// JWT bearer authentication against Duende IdentityServer (Phase 3).
// NameClaimType is set to "username" so that User.Identity!.Name returns the
// username claim emitted by IdentityServer — this keeps Seller stamping and
// ownership checks consistent throughout the controller.
//
// ValidateAudience is false because the IdentityServer resource configuration
// is added in Phase 3; enabling it now would reject all tokens with no audience.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
    });

builder.Services.AddAuthorization();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

await DbInitializer.InitDbAsync(app.Services);

// UseExceptionHandler is registered first so it wraps the entire remaining pipeline —
// any unhandled exception from authentication, authorization, controllers, or elsewhere
// downstream is caught by GlobalExceptionHandler and returned as a ProblemDetails 500.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ── OpenAPI document + Scalar UI (Task 16) ────────────────────────────────────
//
// Mapped unconditionally (not dev-only) — see Docs/Tasks.md DoD and Architecture.md §10.
// MapOpenApi() serves the raw document at /openapi/v1.json; MapScalarApiReference()
// serves the interactive Scalar UI at /scalar, pointed at that document.

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
});

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
