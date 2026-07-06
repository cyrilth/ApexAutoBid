using AuctionService.API.Handlers;
using AuctionService.API.OpenApi;
using AuctionService.Application.Configuration;
using AuctionService.Application.Extensions;
using AuctionService.Infrastructure.Data;
using AuctionService.Infrastructure.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (DbContext + repositories) ───────────────────────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Options validation (fail fast on misconfiguration) ───────────────────────
//
// AddInfrastructureServices binds ImagesOptions/MinioOptions from configuration; here we
// layer DataAnnotations validation + ValidateOnStart on top so a missing or malformed
// Images:PublicBaseUrl, Minio endpoint, or MinIO credential aborts startup with a clear
// OptionsValidationException — rather than silently producing non-absolute object URLs
// (which also breaks platform-hosted image detection in gallery validation) or a broken
// S3 client. Wired here in the composition root because ValidateOnStart lives in the
// ASP.NET Core shared framework, keeping the Infrastructure class library free of extra
// hosting/DataAnnotations package references.

builder.Services.AddOptions<ImagesOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<MinioOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

    // Tags MassTransit's auto-registered bus health check "ready" so it is included in the
    // GET /health/ready predicate below (Task 21 / Requirements §13.4) — it reports unhealthy
    // whenever the RabbitMQ broker connection is down, giving us RabbitMQ readiness for free
    // without a second, redundant broker connection.
    x.ConfigureHealthCheckOptions(options => options.Tags.Add("ready"));

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitPort = builder.Configuration.GetValue<ushort?>("RabbitMq:Port") ?? 5672;
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", rabbitPort, "/", host =>
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

// ── Health checks (Task 21) ──────────────────────────────────────────────────
//
// GET /health/live reports 200 as soon as the process is up (no checks — mapped with
// Predicate = _ => false below). GET /health/ready fans out to the "ready"-tagged checks:
// PostgreSQL via AspNetCore.HealthChecks.NpgSql, and RabbitMQ via MassTransit's own bus
// health check (tagged "ready" above). The connection string is resolved through a deferred
// service-provider factory — not an eager builder.Configuration.GetConnectionString(...) —
// because the integration test host overrides ConnectionStrings:DefaultConnection via
// configuration that is only visible after the host is built (see AddInfrastructureServices).

builder.Services.AddHealthChecks()
    .AddNpgSql(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: ["ready"]);

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

// ── OpenAPI + Scalar (Task 16; OAuth2 login Task 13) ──────────────────────────
//
// BearerSecuritySchemeTransformer declares the "Bearer" HTTP security scheme (the
// generator does not infer it from [Authorize]) and stamps Info.Title/Version.
// OAuth2SecuritySchemeTransformer additionally declares an "OAuth2" scheme describing the
// authorization-code+PKCE flow against Duende's `scalar` client (Task 13). Both schemes
// coexist deliberately — see AuthorizeOperationTransformer's remarks. AuthorizeOperationTransformer
// then attaches both schemes, as alternative requirements, only to operations whose endpoint
// actually requires authorization.

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
    options.AddOperationTransformer<AuthorizeOperationTransformer>();
});

// ── Authentication / Authorization ───────────────────────────────────────────
//
// JWT bearer authentication against Duende IdentityServer (Phase 3).
// NameClaimType is set to "username" so that User.Identity!.Name returns the
// username claim emitted by IdentityServer — this keeps Seller stamping and
// ownership checks consistent throughout the controller.
//
// No RoleClaimType override is needed for User.IsInRole("admin") to work — and
// adding one ("role") actively BREAKS it (verified live during Task 7).
// JwtBearerOptions' default token handler sets MapInboundClaims from
// JwtSecurityTokenHandler.DefaultMapInboundClaims, which defaults to TRUE (confirmed
// via decompilation — NOT JsonWebTokenHandler's own static default, which is false;
// JwtBearerOptions deliberately overrides it). With mapping on, the inbound "role"
// claim Duende stamps (IdentityService's ProfileService uses JwtClaimTypes.Role =
// "role") is auto-remapped to the long ClaimTypes.Role URI — exactly what
// User.IsInRole's default RoleClaimType already checks. (The same default mapping is
// why the controller can read the seller's email via the plain ClaimTypes.Email
// constant even though Duende's wire claim is the short "email" — no NameClaimType-style
// override was ever needed for it. "username" and "email_verified" aren't standard
// short claim names in that legacy map, which is why NameClaimType alone still needs
// the explicit override below, and why the controller reads "email_verified" as a
// literal string.)
//
// ValidateAudience is now enabled (Task 7 — the precondition the original comment
// deferred on is resolved: IdentityService's "apexautobid" ApiResource/ApiScope,
// Phase 3 Task 3, is live, and real tokens have carried aud=apexautobid since Tasks
// 3–5's verification). ValidAudience is a hardcoded literal rather than shared via a
// project reference to IdentityService.Config: these are independently deployable
// services that must not reference each other's code, so "apexautobid" is a repo
// convention (Architecture.md §5.1/§5.5), not a shared constant.
//
// ValidTypes restricts accepted tokens to Duende's access-token typ header
// ("at+jwt", per RFC 9068 — verified against a real minted token's header during
// this task's live verification, not assumed) so an id_token cannot be replayed as
// an access token against this API.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidAudience = "apexautobid";
        options.TokenValidationParameters.NameClaimType = "username";
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
    });

// Phase 3 Task 19 — "EmailVerified" policy, replacing the ad-hoc per-endpoint
// `User.FindFirstValue("email_verified") is not "true"` check that used to live independently in
// AuctionsController.CreateAuction/CreateUploadUrl/CreateThumbnail (the task's original premise —
// "currently only on create" — turned out to be stale; a follow-up round confirmed and converted
// all three ad-hoc call sites, not just one). This is now THE single mechanism enforcing
// email-verified on all five mutating endpoints. RequireClaim's value comparison is
// StringComparer.Ordinal (decompile-confirmed against ClaimsAuthorizationRequirement) — an exact
// match against the literal lowercase "true", identical to every ad-hoc check's `is not "true"`
// pattern match. That value is verified, not assumed, against IdentityService's
// Services/ProfileService.cs: `new(JwtClaimTypes.EmailVerified, user.EmailConfirmed ? "true" :
// "false", ...)` — a plain C# ternary, always lowercase, no JSON-boolean-capitalization concern
// (unlike the Google email_verified claim mapping elsewhere in IdentityService, which is a
// different code path). "email_verified" itself is also confirmed to arrive as the literal short
// claim type: JwtBearer's MapInboundClaims default-true legacy remap table (Task 7) does not
// contain an entry for it, so it is never rewritten to a ClaimTypes.* URI. RequireAuthenticatedUser()
// is included for self-documenting clarity — decompile-confirmed (PolicyEvaluator.AuthorizeAsync)
// that the 401-vs-403 split is actually driven by whether AUTHENTICATION itself succeeded,
// independent of the policy's own requirements, so this isn't strictly load-bearing for that
// split, but it makes the policy's intent explicit without relying on that framework subtlety.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("EmailVerified", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("email_verified", "true"));

// Phase 3 Task 19 follow-up — restores the Warning-level rejection logging every ad-hoc check
// used to do, lost when enforcement moved into the policy above. Decompiled end-to-end
// (AuthorizationMiddleware -> PolicyEvaluator -> the framework's own
// Microsoft.AspNetCore.Authorization.Policy.AuthorizationMiddlewareResultHandler): a RequireClaim
// failure logs nothing at any level — the default handler's only job is ChallengeAsync/
// ForbidAsync, never ILogger. See Handlers/LoggingAuthorizationMiddlewareResultHandler.cs's own
// remarks for the full design (delegates to a real default-handler instance for the actual
// response; only ever logs Forbidden, i.e. policy rejections — never Challenged/401, and never
// the ownership-check Forbid() calls in UpdateAuction/DeleteAuction, which bypass this handler
// entirely). Registered as a plain AddSingleton (not AddAuthorization's own TryAddTransient —
// decompile-confirmed that's the framework's actual registration for this interface, correcting
// an initial assumption that it was singleton; this class is provably stateless so singleton is
// safe and marginally cheaper, a deliberate, disclosed divergence from the framework's own
// choice) AFTER AddAuthorizationBuilder() above: TryAddTransient only adds a descriptor if none
// already exists, and a plain Add call afterward appends a second one for the same interface —
// .NET's DI container resolves the LAST-registered descriptor for a non-enumerable service type,
// so this one wins regardless of TryAdd having already run inside AddAuthorizationBuilder().
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, LoggingAuthorizationMiddlewareResultHandler>();

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

// ── Health endpoints (Task 21) ────────────────────────────────────────────────
//
// Anonymous per Requirements §13.4. /health/live never runs a check (Predicate = _ => false)
// so it reflects only "is the process up". /health/ready only runs checks tagged "ready"
// (PostgreSQL + the MassTransit/RabbitMQ bus check registered above).

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

// ── OpenAPI document + Scalar UI (Task 16; OAuth2 login Task 13) ─────────────
//
// Mapped unconditionally (not dev-only) — see Docs/Tasks.md DoD and Architecture.md §10.
// MapOpenApi() serves the raw document at /openapi/v1.json; MapScalarApiReference()
// serves the interactive Scalar UI at /scalar, pointed at that document.
//
// AddAuthorizationCodeFlow wires Scalar's "Authorize" button to the "OAuth2" security scheme
// (OAuth2SecuritySchemeTransformer) via Duende's public `scalar` client (IdentityService's
// Config.cs), so clicking Authorize runs the real IdentityServer login (authorization code +
// PKCE) instead of requiring a manually pasted token — the Phase 3 acceptance criterion for
// this task. AuthorizationUrl/TokenUrl are config-driven (IdentityServiceUrl), matching the
// AddJwtBearer Authority below. WithRedirectUri pins an explicit, absolute value rather than
// leaving it to Scalar's own default (window.location.origin + pathname — confirmed by
// decompiling/decompressing Scalar.AspNetCore 2.16.7's embedded scalar.js bundle): the docs
// page's landing URL is ambiguous depending on how the user navigated there (/scalar,
// /scalar/, /scalar/v1 are all reachable depending on trailing-slash/document-name routing),
// so a fixed value removes that ambiguity. Three values below must match the `scalar` client
// registration in IdentityService's Config.cs exactly (no shared constant is possible — the
// services deliberately don't reference each other's code): the client id ("scalar"), the
// redirect URI (the client's sole RedirectUris entry), and the selected scope list (the
// client's AllowedScopes).

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
                .WithRedirectUri("http://localhost:5054/scalar")
                .WithSelectedScopes(["openid", "profile", "apexautobid"]);
        });
        options.AddPreferredSecuritySchemes("OAuth2");
    }
});

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
