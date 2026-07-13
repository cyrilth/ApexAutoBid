using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using GatewayService.Handlers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── YARP reverse proxy (Task 2) ──────────────────────────────────────────────
//
// Route definitions (paths, HTTP methods, per-route AuthorizationPolicy, ClusterId) live in
// appsettings.json — they are structural and identical across every environment, matching
// Architecture.md §7's repo-tree comment ("appsettings.json # YARP route configuration").
// Cluster destination addresses, by contrast, are environment-specific network locations (dev
// localhost ports today; docker-compose service DNS names / Kubernetes ClusterIP names later)
// — mirroring every other service's "IdentityServiceUrl" / connection-string convention
// (CLAUDE.md "Configuration": dev values go in appsettings.Development.json, appsettings.json
// holds only non-sensitive, environment-agnostic defaults), they live in
// appsettings.Development.json instead of here.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ── JWT bearer authentication at the edge (Task 3) ───────────────────────────
//
// Identical wiring to every other JwtBearer consumer in the platform (Architecture.md §5.5):
// Authority from config (dev: https://localhost:5001), ValidAudience is the platform-wide
// "apexautobid" ApiScope/ApiResource, NameClaimType "username", and ValidTypes restricted to
// Duende's "at+jwt" access-token typ header so an id_token can't be replayed here either.
//
// The gateway deliberately does NOT layer an "EmailVerified" policy on top the way
// AuctionService.API does — Requirements §3.5 scopes the gateway to validating the JWT itself;
// the email-verified requirement stays enforced in the owning services (Auction, Bidding).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidAudience = "apexautobid";
        options.TokenValidationParameters.NameClaimType = "username";
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];

        // ── Edge 401/403 as ProblemDetails (Task 10 / Requirements §13.1) ────────────
        //
        // The gateway is the one place in the platform where an edge 401/403 must come back
        // as application/problem+json even though no exception was ever thrown
        // (GlobalExceptionHandler only handles genuinely unhandled exceptions). OnChallenge
        // fires when authentication itself fails (missing/invalid/expired token); OnForbidden
        // fires when authentication succeeded but the route's AuthorizationPolicy ("authenticated",
        // registered below) was not met — practically unreachable today since that policy only
        // requires an authenticated user, but wired for correctness/completeness per the task.
        //
        // CRITICAL: both events fire only from the gateway's OWN authentication/authorization
        // middleware, which runs BEFORE MapReverseProxy()'s forwarder ever sees the request —
        // so a request that fails here never reaches a downstream service, and a response that
        // DID come back from a downstream service never passes through this code at all. YARP's
        // forwarder streams proxied responses back unchanged, satisfying the requirement that
        // downstream errors are never rewrapped.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                // Suppresses JwtBearerHandler's default response (empty body + WWW-Authenticate
                // header only) so a ProblemDetails body can be written instead.
                context.HandleResponse();

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                // HandleResponse() also suppresses the WWW-Authenticate header a 401 must carry
                // (RFC 9110 §11.6.1) — rebuild it exactly as JwtBearerHandler's default challenge
                // would have: bare "Bearer" when no token was presented, plus the OAuth error
                // attributes (already populated on the event context before it fires) when a
                // presented token failed validation.
                var challenge = "Bearer";
                if (!string.IsNullOrEmpty(context.Error))
                {
                    challenge += $" error=\"{context.Error}\"";
                }
                if (!string.IsNullOrEmpty(context.ErrorDescription))
                {
                    challenge += $", error_description=\"{context.ErrorDescription}\"";
                }
                context.Response.Headers.WWWAuthenticate = challenge;

                // Rejection-path visibility (phase-end code review follow-up) — method + path
                // only, via message-template logging (never string interpolation). NEVER logs
                // the token/Authorization header or any claim value (Requirements §13.5): this
                // fires precisely when authentication FAILED, so there is no trustworthy claim
                // to log anyway. Resolved from RequestServices per-callback, same as
                // IProblemDetailsService just below — this file has no constructor to inject an
                // ILogger<T> into (top-level statements), and ILoggerFactory.CreateLogger(typeof(Program))
                // mirrors the category-resolution pattern already used elsewhere in the platform
                // (e.g. SearchService.Infrastructure/Data/DbInitializer.cs).
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(Program))
                    .LogWarning(
                        "Authentication challenge (401) rejected {Method} {Path}",
                        context.HttpContext.Request.Method, context.HttpContext.Request.Path);

                var problemDetailsService = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "Unauthorized",
                        Detail = "Authentication is required to access this resource.",
                    },
                });
            },
            OnForbidden = async context =>
            {
                // JwtBearerHandler has already set Response.StatusCode = 403 by the time this
                // runs (HandleForbiddenAsync); only the response body needs replacing.
                context.Response.ContentType = "application/problem+json";

                // Rejection-path visibility (phase-end code review follow-up) — same
                // method/path-only, message-template logging as OnChallenge above; never the
                // token/Authorization header or any claim value (Requirements §13.5).
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(Program))
                    .LogWarning(
                        "Authorization forbidden (403) rejected {Method} {Path}",
                        context.HttpContext.Request.Method, context.HttpContext.Request.Path);

                var problemDetailsService = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Forbidden",
                        Detail = "You do not have permission to access this resource.",
                    },
                });
            },
        };
    });

// A single "authenticated" policy, referenced by name from the mutating-route entries in
// appsettings.json's ReverseProxy:Routes (AuthorizationPolicy: "authenticated") — YARP applies
// it to those routes' endpoints exactly like an [Authorize("authenticated")] attribute would
// (see Yarp.ReverseProxy.Routing.ProxyEndpointFactory). Read routes instead set
// AuthorizationPolicy: "anonymous" — YARP's own reserved value (matched case-insensitively),
// which adds [AllowAnonymous] and explicitly bypasses any ambient fallback authorization
// policy, rather than referencing a policy defined here.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

// ── Rate limiting at the edge (Task 8/8.1) ───────────────────────────────────
//
// Two named fixed-window policies, each partitioned per client IP (Requirements §3.5's "per
// client IP" fixed window) so one caller's usage is never counted against another's. "general"
// is referenced from every proxied route's "RateLimiterPolicy" in appsettings.json (plus
// applied directly to GET api/version and /scalar below, since those two endpoints are
// handled by the gateway itself, not proxied); "strict" is referenced only from the mutating
// auction/bid routes (POST api/bids, POST/PUT/DELETE api/auctions) — same "attach a named
// policy per route from config" pattern as AuthorizationPolicy above, just for a different
// piece of ASP.NET Core middleware. Health endpoints never carry either policy — see the
// explicit .DisableRateLimiting() calls below (Requirements §13.4: excluded from rate
// limiting entirely, not merely given a generous limit).
//
// PermitLimit/window come from configuration (RateLimiting:General / RateLimiting:Mutating in
// appsettings.json) — non-sensitive, environment-agnostic defaults, never hardcoded.
// QueueLimit is 0 for both: queueing HTTP requests behind a rate limit has no benefit for a
// stateless API gateway (no batching/fairness requirement to satisfy) — a caller over the
// limit should get an immediate 429, not one delayed until a queue slot frees up.
//
// GetClientIp (declared below, alongside GetPlatformVersion) reads Connection.RemoteIpAddress
// directly rather than an X-Forwarded-For header. When Nginx fronts the gateway (Phase 8's
// docker-compose stack), that address is recovered from X-Forwarded-For by the framework's
// auto-registered ForwardedHeadersMiddleware — enabled there via the standard
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true container env var, which trusts ANY immediate
// peer (empty KnownProxies/KnownNetworks). That unrestricted trust means a caller who can
// reach the gateway WITHOUT going through Nginx (the compose stack's loopback-only
// host-port 6001 convenience mapping) can spoof X-Forwarded-For and dodge per-IP rate
// limiting — acceptable for a loopback-only dev port, but Phase 9 (Kubernetes) should pin
// KnownProxies/KnownNetworks to the real ingress instead of using the blanket env var.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    // 503 is RateLimiterOptions' own framework default; 429 is what Requirements §3.5/§13.1
    // actually calls for. Set here as a fallback — OnRejected below always sets it explicitly
    // too, and per RateLimiterOptions.RejectionStatusCode's own doc remarks, whatever OnRejected
    // sets always "wins" over this default anyway.
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var generalLimits = builder.Configuration.GetSection("RateLimiting:General");
    rateLimiterOptions.AddPolicy("general", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = generalLimits.GetValue("PermitLimit", 100),
            Window = TimeSpan.FromSeconds(generalLimits.GetValue("WindowSeconds", 60)),
            QueueLimit = 0,
        }));

    var mutatingLimits = builder.Configuration.GetSection("RateLimiting:Mutating");
    rateLimiterOptions.AddPolicy("strict", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = mutatingLimits.GetValue("PermitLimit", 10),
            Window = TimeSpan.FromSeconds(mutatingLimits.GetValue("WindowSeconds", 60)),
            QueueLimit = 0,
        }));

    // ── Over-limit response as ProblemDetails (Task 8.1 / Requirements §13.1) ────────────
    //
    // Same shape as the JwtBearerEvents.OnChallenge/OnForbidden handlers above: a 429 here is
    // a gateway-generated error (no downstream service was ever called), so it must be
    // application/problem+json with a traceId — not RateLimitingMiddleware's default empty
    // body. traceId itself is stamped by AddProblemDetails' CustomizeProblemDetails below,
    // since this also goes through IProblemDetailsService.TryWriteAsync. RetryAfter is
    // included whenever the limiter reports one — FixedWindowRateLimiter always does on
    // rejection (time remaining until its window resets) — satisfying this task's "include
    // Retry-After if natural".
    rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            // Ceiling, not truncation — rounding down would invite clients to retry up to a
            // second before the fixed window actually resets, earning a pointless second 429.
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // Rejection-path visibility (phase-end code review follow-up) — method + path, plus the
        // rate-limit policy dimension ("general"/"strict"), never the client's IP/identity.
        // Endpoint routing has already run by this point in the pipeline (see this file's own
        // "Middleware order" comment above UseRateLimiter()), so the named policy attached to
        // the matched endpoint (EnableRateLimitingAttribute — set here via AddPolicy/
        // RequireRateLimiting, and via YARP's RateLimiterPolicy route config) is already cheaply
        // available from endpoint metadata, no extra lookup required.
        var policyName = context.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(Program))
            .LogWarning(
                "Rate limit exceeded (429) for {Method} {Path} under policy {Policy}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, policyName ?? "unknown");

        var problemDetailsService = context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "Rate limit exceeded. Please try again later.",
            },
        });
    };
});

// ── Global error handling (Task 10) ──────────────────────────────────────────
//
// Same wiring as every other service (Requirements §13.1): AddProblemDetails stamps a traceId
// extension on every ProblemDetails this service writes — including the OnChallenge/OnForbidden
// bodies above, since they also go through IProblemDetailsService.TryWriteAsync — and
// GlobalExceptionHandler converts genuinely unhandled exceptions into a 500 ProblemDetails
// (full exception in Development, generic message in Production).
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── Health checks (Task 11) ──────────────────────────────────────────────────
//
// Deliberately no dependency checks registered — Requirements §13.4 calls out the gateway
// specifically as having no downstream fan-out ("a slow backend service must not mark the
// gateway unhealthy"). Both /health/live and /health/ready below therefore only ever report
// whether the gateway process itself is up.
builder.Services.AddHealthChecks();

var app = builder.Build();

// UseExceptionHandler is registered first so it wraps the entire remaining pipeline — any
// unhandled exception from rate limiting, authentication, authorization, or the proxy/
// endpoints below is caught by GlobalExceptionHandler and returned as a ProblemDetails 500.
app.UseExceptionHandler();

// ── Middleware order: rate limiting BEFORE authentication (Task 8/8.1) ───────────────────
//
// Deliberate, not incidental — both rate limiter policies partition purely by client IP
// (GetClientIp below), never by identity, so nothing here depends on authentication having
// already run. Rate limiting is edge/network-layer flood protection: it should reject an
// over-limit caller as cheaply as possible, before spending any CPU on JWT parsing/validation
// (UseAuthentication) or authorization policy evaluation (UseAuthorization), and before a
// request flood against a protected mutating route can be used to hammer the authentication
// pipeline itself. Concrete, verified consequence: an unauthenticated flood of
// POST /api/auctions that trips the "strict" policy gets 429 — never the 401 that an
// authenticated-only request would otherwise receive — because RateLimitingMiddleware runs
// and rejects the request before AuthenticationMiddleware/AuthorizationMiddleware ever run.
// (Endpoint routing itself still happens first, ahead of every app.Use* call in this file,
// via the minimal-hosting-model's implicit UseRouting/UseEndpoints — so the per-route
// RateLimiterPolicy metadata from appsettings.json is already available here regardless of
// this ordering choice.)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

// ── GET api/version (Task 9) ──────────────────────────────────────────────────
//
// Handled by the gateway itself — not proxied (Architecture.md §3.5 / Docs/Versioning.md §5).
// Reads the platform version from this assembly's own metadata (flowed from
// backend/Directory.Build.props's <Version> via MSBuild) — never hardcoded.
// RequireRateLimiting("general") applies the same policy every other read/anonymous proxied
// route gets (Task 8.1) — this endpoint bypasses YARP entirely, so it can't pick up a
// RateLimiterPolicy from appsettings.json's ReverseProxy:Routes the way proxied routes do.
app.MapGet("api/version", () => Results.Ok(new { version = GetPlatformVersion() }))
    .AllowAnonymous()
    .RequireRateLimiting("general");

// ── Health endpoints (Task 11) ────────────────────────────────────────────────
//
// Anonymous per Requirements §13.4. /health/live never runs a check (Predicate = _ => false),
// so it reflects only "is the process up". /health/ready filters to "ready"-tagged checks —
// none are registered above, so it is vacuously healthy too; the two endpoints still exist
// separately as the two distinct probes orchestrators expect, even though neither does
// dependency fan-out (this task's explicit, deliberate design — see the AddHealthChecks()
// comment above).
//
// .DisableRateLimiting() is explicit and load-bearing for Requirements §13.4's "excluded from
// rate limiting" (not merely "given a generous limit") — no GlobalLimiter is configured above,
// so these two endpoints would already go unlimited by omission alone, but an explicit call
// makes that exclusion self-documenting and keeps it true even if a GlobalLimiter is ever
// added later for some other reason.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous()
    .DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous()
    .DisableRateLimiting();

// ── Aggregated OpenAPI docs + single Scalar UI (Task 7.1–7.3) ────────────────────────────
//
// The gateway has no OpenAPI document of its own to generate (no AddOpenApi()/MapOpenApi()
// call anywhere in this file) — it only aggregates documents YARP already proxies from each
// service at /openapi/{svc}/v1.json (the "openapi-auction"/"openapi-search" routes in
// appsettings.json, Task 7.1). MapScalarApiReference's page fetches those documents directly
// from the BROWSER, same-origin, at request time — nothing here needs a local document to
// point at. RequireRateLimiting("general") mirrors GET api/version above: this endpoint is
// mapped directly on the gateway (not a YARP route), so it can't inherit a RateLimiterPolicy
// from appsettings.json the way the proxied /openapi/*.json routes do.
//
// WithOpenApiRoutePattern's "{documentName}" placeholder is substituted with each AddDocument
// call's first argument, producing exactly the two proxied paths from Task 7.1
// ("/openapi/auction/v1.json", "/openapi/search/v1.json") — adding a third service's docs
// later (Bidding/Identity admin) needs one more AddDocument call here plus one more YARP route
// in appsettings.json, no other change.
//
// AddAuthorizationCodeFlow wires Scalar's "Authorize" button to the "OAuth2" security scheme
// each downstream document itself declares (AuctionService.API's OAuth2SecuritySchemeTransformer
// today; SearchService.API's document has no protected operations yet, so it simply has
// nothing for a security scheme to attach to) via the same Duende `scalar` public client
// (IdentityService's Config.cs) — one login now covers every "try it" request across every
// aggregated document, this task's Phase 4 acceptance criterion. AuthorizationUrl/TokenUrl are
// config-driven (IdentityServiceUrl), matching AddJwtBearer's Authority above.
// WithRedirectUri pins an explicit value — http://localhost:6001/scalar, the gateway's own dev
// origin and path — rather than Scalar's ambiguous same-page default (see
// AuctionService.API's Program.cs comment for the full reasoning, decompile-verified there).
// This exact value, the client id ("scalar"), and the selected scope list must match a
// RedirectUris/AllowedScopes entry on IdentityService's `scalar` client (Config.cs) — that
// client now lists BOTH this gateway's redirect URI and AuctionService's own 5054 one, since
// either docs page can run the same login.
var identityServiceUrl = builder.Configuration["IdentityServiceUrl"];

app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/openapi/{documentName}/v1.json");

    options.AddDocument("auction", "Auction Service");
    options.AddDocument("search", "Search Service", isDefault: true);
    // Phase 5 Task 18 — same "openapi/{svc}/v1.json" proxied-path convention as the two
    // documents above (the "openapi-bidding" route, appsettings.json).
    options.AddDocument("bidding", "Bidding Service");

    // ── Route "try it" through the gateway itself, not the downstream service (small fix) ──
    //
    // Each proxied document (AuctionService.API's, SearchService.API's own Program.cs) still
    // declares its OWN absolute "servers" entry pointing at that service's own dev URL (e.g.
    // http://localhost:5054) — Scalar.AspNetCore's own remarks for ScalarOptions.Servers say
    // this list "will override the servers defined in the OpenAPI document", so AddServer("/")
    // here replaces THAT entry, for every aggregated document, with a single relative one,
    // entirely at the gateway side — the downstream services' own Program.cs/servers config is
    // untouched. WithDynamicBaseServerUrl(true) is what actually makes the relative "/"
    // resolve: per its own doc remarks it "only works for relative server URLs" and, when
    // enabled, derives the base from the CURRENT REQUEST's own origin rather than a fixed
    // BaseServerUrl — i.e. whatever scheme+host the browser used to load /scalar (localhost:6001
    // today, the gateway's docker-compose/Kubernetes address later) — so nothing here hardcodes
    // http://localhost:6001, and "try it" requests hit the gateway's own JWT/rate-limiting/
    // ProblemDetails edge instead of bypassing it straight to the downstream service.
    options.AddServer("/");
    options.WithDynamicBaseServerUrl(true);

    if (!string.IsNullOrWhiteSpace(identityServiceUrl))
    {
        // Phase 8: the pinned redirect URI is derived from the gateway's public origin, which
        // stopped being a constant once docker-compose put the gateway behind Nginx at
        // https://api.apexautobid.local. Scalar:PublicOrigin defaults to the dotnet-run dev
        // origin; whatever value is used must have a matching {origin}/scalar RedirectUris
        // entry (and the origin itself in AllowedCorsOrigins) on IdentityService's `scalar`
        // client — now also config-driven there (Config.GetClients's Clients:Scalar:Origins).
        var scalarPublicOrigin =
            (builder.Configuration["Scalar:PublicOrigin"] ?? "http://localhost:6001").TrimEnd('/');

        options.AddAuthorizationCodeFlow("OAuth2", flow =>
        {
            flow.WithAuthorizationUrl($"{identityServiceUrl}/connect/authorize")
                .WithTokenUrl($"{identityServiceUrl}/connect/token")
                .WithClientId("scalar")
                .WithPkce(Pkce.Sha256)
                .WithRedirectUri($"{scalarPublicOrigin}/scalar")
                .WithSelectedScopes(["openid", "profile", "apexautobid"]);
        });
        options.AddPreferredSecuritySchemes("OAuth2");
    }
})
    .AllowAnonymous()
    .RequireRateLimiting("general");

app.Run();

// Reads the platform version from AssemblyInformationalVersionAttribute (flows from
// Directory.Build.props's <Version>, e.g. "0.1.0" — or "0.1.0+<sha>" on a deterministic/
// SourceLink-enabled CI build), stripping any "+<commit>" build-metadata suffix so the response
// is always the bare semver (Docs/Versioning.md's file-based source of truth, not a commit).
// Falls back to the four-part AssemblyVersion if the informational attribute is somehow absent.
static string GetPlatformVersion()
{
    var assembly = Assembly.GetExecutingAssembly();
    var informationalVersion = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;

    if (!string.IsNullOrWhiteSpace(informationalVersion))
    {
        var plusIndex = informationalVersion.IndexOf('+');
        return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
    }

    return assembly.GetName().Version?.ToString() ?? "0.0.0";
}

// Partition key for both rate limiter policies above (Task 8/8.1) — the caller's remote IP
// address, per Requirements §3.5's "per client IP" fixed window. Falls back to a shared
// constant key for the (practically unreachable outside unit/test hosts) case where
// RemoteIpAddress is null, so every such request is still limited together rather than each
// one bypassing the limiter under a distinct null partition.
static string GetClientIp(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// Exposes the implicit Program class (top-level statements) to a future integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
