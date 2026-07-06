using BiddingService.API.Handlers;
using BiddingService.API.OpenApi;
using BiddingService.API.Services;
using BiddingService.Application.Configuration;
using BiddingService.Application.Extensions;
using BiddingService.Application.Services;
using BiddingService.Infrastructure.Data;
using BiddingService.Infrastructure.Extensions;
using BiddingService.Infrastructure.Grpc;
using BiddingService.Infrastructure.Protos;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using MongoDB.Driver;
using Polly;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (MongoDB connection + repositories + bus-outbox wiring) ─────────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster + IBidService + IAuctionProvider) ─────────────────────────

builder.Services.AddApplicationServices();

// ── Options validation (fail fast on misconfiguration) ───────────────────────────────────────
//
// AddInfrastructureServices binds FinalizationOptions from configuration (Critical 2's grace
// period); ValidateDataAnnotations + ValidateOnStart layered on top here so an out-of-range
// Bidding:FinalizationGraceSeconds aborts startup with a clear OptionsValidationException,
// mirroring AuctionService.API's identical ImagesOptions/MinioOptions convention.

builder.Services.AddOptions<FinalizationOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── gRPC fallback client + Polly resilience (Phase 5 Tasks 6/7 — Requirements §3.3/§13.5) ────
//
// AppContext switch: the dev Auction Service gRPC endpoint (http://localhost:7054 —
// AuctionService.API's appsettings.Development.json) is deliberately cleartext HTTP/2 ("h2c"),
// not HTTPS — SocketsHttpHandler otherwise requires TLS ALPN negotiation to use HTTP/2 at all,
// and the very first call fails outright. Set only when the configured URL is actually
// cleartext http:// (an https endpoint negotiates HTTP/2 via ALPN and never needs it), and
// before the channel's first request; setting it here, at startup, well before AddGrpcClient
// below ever dispatches anything, is sufficient (live-verified against the running
// AuctionService.API instance during this task — still required on this .NET 10 build exactly
// as documented for earlier versions).
var auctionGrpcUrl = new Uri(builder.Configuration["Grpc:AuctionServiceUrl"]
    ?? throw new InvalidOperationException("Grpc:AuctionServiceUrl is not configured"));

if (auctionGrpcUrl.Scheme == Uri.UriSchemeHttp)
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

builder.Services
    .AddGrpcClient<Auctions.AuctionsClient>(options => options.Address = auctionGrpcUrl)
    // Microsoft.Extensions.Http.Resilience (Polly v8) — retries with backoff on transient
    // gRPC/HTTP failures (HttpRetryStrategyOptions' default ShouldHandle predicate already
    // covers HttpRequestException, request timeouts, and 5xx/408/429 responses — exactly the
    // "transient failure" surface a dropped/slow connection to the Auction Service would
    // produce). OnRetry logs at Warning — the exact level Requirements §13.5's log-level table
    // calls out for "gRPC/HTTP fallback retries" — rather than relying on the standard
    // resilience handler's own default telemetry levels, so this is guaranteed regardless of
    // Polly's own defaults.
    .AddResilienceHandler("auction-grpc-fallback", (pipelineBuilder, context) =>
    {
        pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = args =>
            {
                context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("BiddingService.Infrastructure.Grpc.AuctionGrpcClient")
                    .LogWarning(args.Outcome.Exception,
                        "gRPC fallback retry {Attempt}/{MaxAttempts} to Auction Service GetAuction after {Delay}",
                        args.AttemptNumber + 1, 3, args.RetryDelay);
                return default;
            }
        });
    });

// LocalAuctionProvider registered as its own concrete type (not just as IAuctionProvider) so
// GrpcFallbackAuctionProvider's constructor can depend on it directly, sidestepping the
// self-referential-resolution problem a constructor parameter typed IAuctionProvider would hit
// once the registration below overrides IAuctionProvider itself (see
// GrpcFallbackAuctionProvider's remarks). The IAuctionProvider registration below OVERRIDES
// ApplicationServiceExtensions.AddApplicationServices' own IAuctionProvider → LocalAuctionProvider
// registration — .NET's DI container resolves the LAST-registered descriptor for a
// non-enumerable service type — the one and only place, per this task's instructions, where
// that swap happens; BidAppService/BidsController remain entirely unaware of it.
builder.Services.AddScoped<LocalAuctionProvider>();
builder.Services.AddScoped<IAuctionProvider, GrpcFallbackAuctionProvider>();

// ── Messaging (MassTransit + RabbitMQ + MongoDB transactional Outbox) ───────────────────────
//
// AddConsumersFromNamespaceContaining discovers AuctionCreatedConsumer (Task 5) and any future
// consumer added to that namespace — mirrors AuctionService.API's/SearchService.API's identical
// discovery call.
//
// KebabCaseEndpointNameFormatter with the "bidding" prefix produces queue names like
// "bidding-auction-created-consumer", keeping queues identifiable and collision-free on the
// shared RabbitMQ broker alongside "auction-*"/"search-*".
//
// AddMongoDbOutbox + UseBusOutbox() (Task 4/11): configures MassTransit's MongoDB
// transactional "bus outbox" so that IPublishEndpoint.Publish calls made from a scope holding
// an active MongoDbContext transaction (see BidPlacementUnitOfWork) are staged in the same
// Mongo transaction as the bid document write and only delivered to RabbitMQ once that
// transaction commits — live-verified end-to-end (write visibility, rollback, and actual
// message delivery) during this task; see BidPlacementUnitOfWork's remarks for the full
// mechanics. AddConfigureEndpointsCallback additionally applies the CONSUMER-side inbox
// (UseMongoDbOutbox(context)) to every auto-configured receive endpoint (today: just
// AuctionCreatedConsumer's), giving it redelivery-dedup on top of its own idempotent upsert —
// mirrors SearchService.API's identical wiring and "primary guarantee vs. dedup optimization"
// rationale.

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining<BiddingService.Application.Consumers.AuctionCreatedConsumer>();

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("bidding", false));

    // Tags MassTransit's auto-registered bus health check "ready" so it is included in the
    // GET /health/ready predicate below (Task 22 / Requirements §13.4) — mirrors
    // AuctionService.API's/SearchService.API's identical wiring.
    x.ConfigureHealthCheckOptions(options => options.Tags.Add("ready"));

    x.AddMongoDbOutbox(o =>
    {
        o.ClientFactory(provider => provider.GetRequiredService<IMongoClient>());
        o.DatabaseFactory(provider => provider.GetRequiredService<IMongoDatabase>());

        // 30 minutes — MassTransit's own default, made explicit rather than left implicit,
        // matching SearchService's identical choice/rationale for its consumer-side inbox.
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);

        // Matches AuctionService.API's EF outbox QueryDelay — how frequently the bus-outbox
        // delivery worker polls for unsent messages once a transaction has committed.
        o.QueryDelay = TimeSpan.FromSeconds(10);

        o.UseBusOutbox();
    });

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

// ── Health checks (Task 22) ──────────────────────────────────────────────────────────────────
//
// GET /health/live reports 200 as soon as the process is up (no checks — mapped with
// Predicate = _ => false below). GET /health/ready fans out to the "ready"-tagged checks:
// MongoDB via AspNetCore.HealthChecks.MongoDb, and RabbitMQ via MassTransit's own bus health
// check (tagged "ready" above) — mirrors SearchService.API's identical Task 14 wiring.

builder.Services.AddHealthChecks()
    .AddMongoDb(
        sp => sp.GetRequiredService<IMongoDatabase>(),
        name: "mongodb",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));

// ── Controllers ───────────────────────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// ── Global error handling (Task 21) ──────────────────────────────────────────────────────────
//
// AddProblemDetails() makes every error response (400 model-validation failures via
// [ApiController] — e.g. PlaceBidDto.Amount's [Range], 404/400 returned explicitly by
// BidsController, and 500s from GlobalExceptionHandler) RFC 7807 application/problem+json.
// CustomizeProblemDetails stamps a traceId extension on every ProblemDetails this service
// writes, correlating the response back to the corresponding log entry (Requirements §13.1).
// Bid outcomes like TooLow/Finished are normal 200 responses, never exceptions or
// ProblemDetails — see BidsController's remarks.

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── OpenAPI + Scalar (Task 18; OAuth2 login mirrors AuctionService.API's Task 13) ────────────
//
// Same pattern as AuctionService.API's identical block: BearerSecuritySchemeTransformer
// declares the "Bearer" HTTP security scheme (the generator does not infer it from
// [Authorize]) and stamps Info.Title/Version. OAuth2SecuritySchemeTransformer additionally
// declares an "OAuth2" scheme describing the authorization-code+PKCE flow against Duende's
// `scalar` client. AuthorizeOperationTransformer then attaches both schemes, as alternative
// requirements, only to operations whose endpoint actually requires authorization — today,
// just POST api/bids.

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
    options.AddOperationTransformer<AuthorizeOperationTransformer>();
});

// ── Authentication / Authorization (Task 13) ─────────────────────────────────────────────────
//
// JWT bearer authentication against Duende IdentityServer — configured identically to
// AuctionService.API (Architecture.md §5.5, which explicitly calls out that this exact block
// is what Phase 5 Task 13 mirrors). NameClaimType is set to "username" so User.Identity!.Name
// returns the username claim emitted by IdentityServer, keeping Bidder-stamping consistent
// with AuctionsController's identical Seller-stamping convention. ValidTypes restricts
// accepted tokens to Duende's access-token typ header ("at+jwt", RFC 9068) so an id_token
// cannot be replayed as an access token against this API.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidAudience = "apexautobid";
        options.TokenValidationParameters.NameClaimType = "username";
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
    });

// "EmailVerified" policy (Task 13) — applied to POST api/bids only (BidsController); GET
// api/bids/{auctionId} stays anonymous. RequireClaim's value comparison is exact-match against
// the literal lowercase "true" — mirrors AuctionService.API's identical policy verbatim (see
// its Program.cs remarks for the full decompile-confirmed rationale, which applies unchanged
// here since both services validate tokens from the same IdentityServer).

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("EmailVerified", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("email_verified", "true"));

// ── Background auction finalizer (Task 12) ───────────────────────────────────────────────────
//
// See AuctionFinalizerHostedService's own remarks for the full design (PeriodicTimer, one
// fresh DI scope per tick, tick-level error containment on top of
// AuctionFinalizationAppService's own per-auction containment).

builder.Services.AddHostedService<AuctionFinalizerHostedService>();

// ─────────────────────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

await DbInitializer.InitDbAsync(app.Services, app.Lifetime.ApplicationStopping);

// UseExceptionHandler is registered first so it wraps the entire remaining pipeline — any
// unhandled exception from authentication, authorization, controllers, or elsewhere downstream
// is caught by GlobalExceptionHandler and returned as a ProblemDetails 500.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ── Health endpoints (Task 22) ───────────────────────────────────────────────────────────────
//
// Anonymous per Requirements §13.4. /health/live never runs a check (Predicate = _ => false)
// so it reflects only "is the process up". /health/ready only runs checks tagged "ready"
// (MongoDB + the MassTransit/RabbitMQ bus check registered above).

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

// ── OpenAPI document + Scalar UI (Task 18) ───────────────────────────────────────────────────
//
// Mapped unconditionally, same as AuctionService.API. MapOpenApi() serves the raw document at
// /openapi/v1.json; MapScalarApiReference() serves the interactive Scalar UI at /scalar.
//
// AddAuthorizationCodeFlow wires Scalar's "Authorize" button to the "OAuth2" security scheme
// via Duende's public `scalar` client (IdentityService's Config.cs — this task adds a third
// RedirectUris/AllowedCorsOrigins entry there for this service's own dev origin, alongside
// AuctionService's 5054 and the Gateway's 6001). WithRedirectUri pins an explicit, absolute
// value for the same ambiguous-default reason AuctionService.API's identical block documents.

var identityServiceUrl = builder.Configuration["IdentityServiceUrl"];

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/openapi/{documentName}.json");

    if (!string.IsNullOrWhiteSpace(identityServiceUrl))
    {
        options.AddAuthorizationCodeFlow("OAuth2", flow =>
        {
            flow.WithAuthorizationUrl($"{identityServiceUrl}/connect/authorize")
                .WithTokenUrl($"{identityServiceUrl}/connect/token")
                .WithClientId("scalar")
                .WithPkce(Pkce.Sha256)
                .WithRedirectUri("http://localhost:7003/scalar")
                .WithSelectedScopes(["openid", "profile", "apexautobid"]);
        });
        options.AddPreferredSecuritySchemes("OAuth2");
    }
});

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
