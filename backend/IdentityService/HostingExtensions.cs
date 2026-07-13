using System.Globalization;
using System.Threading.RateLimiting;
using Duende.IdentityModel;
using Duende.IdentityServer;
using IdentityService.Data;
using IdentityService.Handlers;
using IdentityService.Models;
using IdentityService.OpenApi;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

namespace IdentityService;

internal static class HostingExtensions
{
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        _ = builder.Services.AddRazorPages();

        _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        _ = builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _ = builder.Services.Configure<IdentityOptions>(options =>
        {
            // Phase 3 Task 14 landmine (a): RequireUniqueEmail was never set (defaults to
            // false — decompile-confirmed in Task 4's review), so duplicate emails were
            // registerable, making confirmation-link/email lookups ambiguous. All four seeded
            // dev users (SeedData.cs) already have unique emails, so enabling this needs no
            // dev-DB cleanup.
            options.User.RequireUniqueEmail = true;

            // Deliberately NOT setting options.SignIn.RequireConfirmedEmail = true here.
            // Requirements.md §3.4 is explicit: "unconfirmed accounts can log in and browse" —
            // but SignInOptions.RequireConfirmedEmail makes SignInManager reject sign-in
            // entirely for unconfirmed users (SignInResult.NotAllowed), which would directly
            // contradict that. Task 14.1's "enable RequireConfirmedEmail" is read here as
            // "enable the confirmed-email FEATURE" (token generation, confirmation page,
            // EmailConfirmed flipping via Pages/Account/ConfirmEmail) rather than this literal
            // Identity flag. The actual 403-on-unverified-email requirement (Task 14.4) is
            // enforced entirely at the application layer: the email_verified access-token claim
            // (Services/ProfileService.cs, dynamically reflects EmailConfirmed — Task 3) plus
            // AuctionService.API's existing ad-hoc 403 checks on its mutating endpoints
            // (AuctionsController.cs, Phase 2 — verified, not modified, by this task).

            // Phase 3 Task 16.3 — account lockout. Explicit rather than relying on the
            // framework defaults (LockoutOptions' own ctor already sets these three EXACT values
            // — decompile-confirmed — so behavior is unchanged either way), specifically so the
            // thresholds are visible here and can be tuned via configuration without a code
            // change. Requirements.md doesn't specify numbers, so the framework's own
            // (well-known, widely-used) defaults are kept as the fallback.
            options.Lockout.MaxFailedAccessAttempts =
                builder.Configuration.GetValue("Identity:Lockout:MaxFailedAccessAttempts", 5);
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(
                builder.Configuration.GetValue("Identity:Lockout:LockoutMinutes", 5));
            // Already the framework default (true) — explicit for documentation. New AND
            // existing accounts alike get LockoutEnabled=true automatically the moment
            // UserManager.CreateAsync runs (decompile-confirmed: UserManager.CreateAsync sets
            // LockoutEnabled=true post-creation whenever this flag is true and the store
            // supports IUserLockoutStore, which the real EF Core store does) — this applies
            // identically to Register/Index.cshtml.cs's new-user path AND SeedData.cs's seed
            // users, both of which go through this exact same CreateAsync call. Live-verified
            // for this task, not assumed.
            options.Lockout.AllowedForNewUsers = true;
        });

        _ = builder.Services
            .AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // Use a large chunk size for diagnostic data in development so long entries
                // aren't split across multiple console log lines.
                if (builder.Environment.IsDevelopment())
                {
                    options.Diagnostics.ChunkSize = 1024 * 1024 * 10; // 10 MB
                }
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryClients(Config.GetClients(builder.Configuration, builder.Environment.IsDevelopment()))
            .AddAspNetIdentity<ApplicationUser>()
            // Replaces AddAspNetIdentity's default IProfileService registration with
            // Services/ProfileService.cs, which adds the username/email/email_verified/role
            // access-token claims (Phase 3 Task 3 / Requirements.md §3.4).
            .AddProfileService<ProfileService>()
            .AddLicenseSummary();

        // The template's demo "Sign-in with demo.duendesoftware.com" OIDC provider has been
        // removed (Phase 3 Task 1 review follow-up) — it rendered a live login button that
        // auto-provisioned local accounts for anyone with a demo.duendesoftware.com login, which
        // has no place in this app even in dev.
        //
        // Phase 3 Task 15 — Google external login, registered ONLY when both environment
        // variables are present (Authentication__Google__ClientId /
        // Authentication__Google__ClientSecret — bound here via the standard
        // "Authentication:Google:ClientId"/"ClientSecret" configuration keys). Real external-
        // provider credentials are NEVER committed in any environment, dev included
        // (Requirements.md §6 — this is the one exception to the "dev secrets are committed"
        // rule) — so appsettings.json/appsettings.Development.json intentionally carry NO
        // "Authentication" section at all; its total absence (not an empty placeholder, unlike
        // Smtp above) is exactly what makes Google login silently absent by default. The Login
        // (Pages/Account/Login/Index.cshtml.cs) and Register (Pages/Account/Register/Index.cshtml.cs)
        // pages both build their external-provider button lists dynamically from whatever
        // schemes IAuthenticationSchemeProvider reports at request time, so with .AddGoogle(...)
        // never called, both pages render with no Google button and
        // Pages/ExternalLogin/Challenge?scheme=Google simply 404s (unknown scheme) rather than
        // 500ing — verified live for this task.
        var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
        var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
        {
            _ = builder.Services.AddAuthentication()
                .AddGoogle(options =>
                {
                    // Matches the removed demo OIDC provider's own SignInScheme (see the
                    // original duende-is-aspid template, Task 1 git history) — required so the
                    // external principal lands in Duende's temporary external cookie, which
                    // Pages/ExternalLogin/Callback.cshtml.cs reads via
                    // HttpContext.AuthenticateAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme).
                    // Without this, the external principal would go to whatever the DEFAULT
                    // authentication scheme is instead, and Callback's AuthenticateAsync call
                    // against the external scheme would fail.
                    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
                    options.ClientId = googleClientId;
                    options.ClientSecret = googleClientSecret;

                    // GoogleOptions' own default ClaimActions (decompile-confirmed against
                    // Microsoft.AspNetCore.Authentication.Google 10.0.9) map nameidentifier,
                    // name, given/family name, and email from Google's OIDC-compliant v3
                    // userinfo endpoint (GoogleDefaults.UserInformationEndpoint) — but NOT
                    // email_verified; it's simply dropped unless mapped explicitly. Needed by
                    // Services/ExternalLoginProvisioningService.cs to decide EmailConfirmed
                    // (Requirements.md §3.4: "Google-asserted verified emails are treated as
                    // confirmed").
                    options.ClaimActions.MapJsonKey(JwtClaimTypes.EmailVerified, "email_verified");

                    // CallbackPath defaults to "/signin-google" (GoogleOptions ctor) — left as
                    // the default. In dev, the Google Cloud Console OAuth client's "Authorized
                    // redirect URI" must be exactly https://localhost:5001/signin-google;
                    // whatever hostname IdentityService is actually reachable at in Phase 7/8's
                    // containerized deployments will need its own matching redirect URI added in
                    // the Google Console (out of scope for this task — no frontend/containerized
                    // deployment exists yet to pin a real value against).
                });
        }

        // add `.PersistKeysTo…()` and `.ProtectKeysWith…()` calls
        // see more at https://docs.duendesoftware.com/general/data-protection
        _ = builder.Services.AddDataProtection()
                   .SetApplicationName("IdentityServer");

        // Phase 3 Task 14.2 — SMTP email sender (Services/SmtpEmailSender.cs), used by the
        // Register page to send the confirmation link. ValidateOnStart fails fast at boot if
        // Smtp:Host/FromAddress/FromName are missing, rather than on the first registration
        // attempt — dev values come from appsettings.Development.json (Mailpit); everywhere
        // else, Smtp__Host/Smtp__Username/Smtp__Password etc. must be supplied as environment
        // variables (Requirements.md §6 — never committed).
        _ = builder.Services
            .AddOptions<SmtpOptions>()
            .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        _ = builder.Services.AddTransient<IEmailSender<ApplicationUser>, SmtpEmailSender>();

        // Phase 3 Task 15 — extracted out of Pages/ExternalLogin/Callback.cshtml.cs's own
        // AutoProvisionUserAsync so the provisioning DECISION logic (duplicate-email rejection,
        // email_verified -> EmailConfirmed) is unit-testable without a PageModel/HttpContext
        // harness. Registered unconditionally (not gated behind the Google env-var check above)
        // — it's provider-agnostic in principle, even though Google is the only caller today.
        _ = builder.Services.AddScoped<ExternalLoginProvisioningService>();

        // Phase 3 Task 16.1/16.2 — Cloudflare Turnstile bot protection on the Register page.
        // Same fail-fast ValidateOnStart shape as SmtpOptions above. Dev/Docker values
        // (appsettings.Development.json) are Cloudflare's own official always-pass test keys —
        // committable by design (unlike Google's credentials, Task 15) because Cloudflare
        // publishes them specifically for this purpose. Production keys are real external
        // credentials, environment-variable-only, never committed (same rule as Google).
        _ = builder.Services
            .AddOptions<TurnstileOptions>()
            .Bind(builder.Configuration.GetSection(TurnstileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Typed HttpClient via IHttpClientFactory (Requirements.md §3.4: "plain HttpClient, no
        // NuGet package needed") — pooled/reused connections and DNS-refresh behavior for free,
        // vs. a bare `new HttpClient()` per call.
        _ = builder.Services.AddHttpClient<ITurnstileValidator, TurnstileValidator>();

        // Phase 3 Task 16.4 — rate limiting for the login, register, and token endpoints
        // (Requirements.md §3.4). A SINGLE GlobalLimiter branching on request path, rather than
        // per-endpoint EnableRateLimitingAttribute/RequireRateLimiting policies: Duende's
        // /connect/token is handled entirely inside IdentityServerMiddleware, not ASP.NET Core
        // endpoint routing, so it has no endpoint metadata to attach a named policy to — a
        // path-based GlobalLimiter is the one mechanism that covers all three endpoints
        // uniformly (this is also .NET's own documented pattern for "some paths get one limit,
        // others a different limit, everything else unlimited" — not a workaround).
        // Read + validate the limits HERE, at boot, not inside the partition factories:
        // FixedWindowRateLimiter's constructor throws ArgumentException for a PermitLimit or
        // Window <= 0, but the factory delegates below run LAZILY on the first matching request
        // — a config typo like "PermitLimit": 0 would surface as a runtime 500 on the first
        // login POST instead of a startup failure (decompile-confirmed in Task 16's review).
        var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
        var loginWindowSeconds = builder.Configuration.GetValue("RateLimiting:Login:WindowSeconds", 60);
        var registerPermitLimit = builder.Configuration.GetValue("RateLimiting:Register:PermitLimit", 5);
        var registerWindowSeconds = builder.Configuration.GetValue("RateLimiting:Register:WindowSeconds", 60);
        var tokenPermitLimit = builder.Configuration.GetValue("RateLimiting:Token:PermitLimit", 30);
        var tokenWindowSeconds = builder.Configuration.GetValue("RateLimiting:Token:WindowSeconds", 60);

        foreach (var (key, value) in new (string Key, int Value)[]
        {
            ("RateLimiting:Login:PermitLimit", loginPermitLimit),
            ("RateLimiting:Login:WindowSeconds", loginWindowSeconds),
            ("RateLimiting:Register:PermitLimit", registerPermitLimit),
            ("RateLimiting:Register:WindowSeconds", registerWindowSeconds),
            ("RateLimiting:Token:PermitLimit", tokenPermitLimit),
            ("RateLimiting:Token:WindowSeconds", tokenWindowSeconds),
        })
        {
            if (value <= 0)
            {
                throw new InvalidOperationException(
                    $"Configuration value '{key}' must be a positive integer; got {value}.");
            }
        }

        _ = builder.Services.AddRateLimiter(options =>
        {
            // Framework default is 503 (decompile-confirmed against RateLimiterOptions) —
            // Requirements.md §3.4 explicitly calls for 429, so this must be set, not assumed.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                // Client IP is the standard partition key for this kind of limiter.
                // Reached directly under `dotnet run`; behind Nginx (Phase 8's docker-compose
                // stack) the real client IP is recovered from X-Forwarded-For by the
                // framework's auto-registered ForwardedHeadersMiddleware
                // (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true in docker-compose.yml — also what
                // lets Duende derive the https public origin as issuer). That env var trusts
                // any immediate peer; fine here because this service has NO direct host port
                // in that topology (Nginx is the only path in). Phase 9 should still pin
                // KnownProxies/KnownNetworks to the real ingress.
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Scoped to POST only — GET requests to these pages (viewing the form) aren't
                // the credential-stuffing/registration-flood/token-grinding threat this guards
                // against; /connect/token only ever responds to POST anyway.
                if (HttpMethods.IsPost(httpContext.Request.Method))
                {
                    if (httpContext.Request.Path.StartsWithSegments("/Account/Login"))
                    {
                        return RateLimitPartition.GetFixedWindowLimiter($"login:{ip}", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = loginPermitLimit,
                            Window = TimeSpan.FromSeconds(loginWindowSeconds),
                            QueueLimit = 0,
                        });
                    }

                    if (httpContext.Request.Path.StartsWithSegments("/Account/Register"))
                    {
                        return RateLimitPartition.GetFixedWindowLimiter($"register:{ip}", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = registerPermitLimit,
                            Window = TimeSpan.FromSeconds(registerWindowSeconds),
                            QueueLimit = 0,
                        });
                    }

                    if (httpContext.Request.Path.StartsWithSegments("/connect/token"))
                    {
                        return RateLimitPartition.GetFixedWindowLimiter($"token:{ip}", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = tokenPermitLimit,
                            Window = TimeSpan.FromSeconds(tokenWindowSeconds),
                            QueueLimit = 0,
                        });
                    }
                }

                // Every other request (GET page views, static files, discovery/JWKS, etc.) is
                // unlimited — GetNoLimiter doesn't allocate a persistent per-key limiter, so this
                // doesn't leak memory across many distinct non-rate-limited callers.
                return RateLimitPartition.GetNoLimiter(ip);
            });
        });

        // Phase 3 Task 17 — global error handling (Requirements §13.1). Mirrors
        // AuctionService.API's/SearchService.API's exact wiring: AddProblemDetails stamps a
        // traceId extension on every ProblemDetails this service writes (correlating a response
        // back to its log entry); GlobalExceptionHandler (Handlers/GlobalExceptionHandler.cs)
        // catches genuinely unhandled exceptions, always logs the full exception, and — for the
        // JSON-facing surface only (see its own remarks for the disclosed HTML/JSON split and
        // why) — returns a 500 ProblemDetails whose Detail is the full exception in Development
        // and a generic message in Production. For a browser-navigation request, it declines
        // (returns false) and ExceptionHandlerMiddleware falls through to re-executing /Error
        // (see ConfigurePipeline's UseExceptionHandler("/Error") call and its own remarks).
        _ = builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            };
        });
        _ = builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        // Phase 3 Task 18 — health endpoints (Requirements §13.4). GET /health/live never runs a
        // check (see ConfigurePipeline's Predicate = _ => false), so it needs no registration
        // here. GET /health/ready fans out to the "ready"-tagged checks — PostgreSQL only for
        // this service (no RabbitMQ/MassTransit, no MongoDB, unlike Auction/Search/Bidding).
        // Mirrors AuctionService.API's exact AddNpgSql call: the connection string is resolved
        // through a deferred service-provider factory (sp => sp.GetRequiredService<IConfiguration>()
        // .GetConnectionString("DefaultConnection")), not an eager builder.Configuration capture
        // — AuctionService's own comment explains why: the integration test host overrides
        // ConnectionStrings:DefaultConnection via configuration that is only visible after the
        // host is built, and the same is true here (CustomWebAppFactory.cs overrides it to the
        // real Testcontainers Postgres). No explicit Timeout is set, matching AuctionService.API's
        // AddNpgSql call exactly (not SearchService.API's AddMongoDb, which does set one) — this
        // task follows AuctionService's established pattern precisely rather than "improving" on
        // it unprompted; the omitted-timeout inconsistency between the two is an existing,
        // already-noted-elsewhere cross-service consistency item, not something to fix here.
        _ = builder.Services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!,
                name: "postgresql",
                tags: ["ready"]);

        // ── Admin user-management API (Phase 11 Task 2 / Requirements.md §10.1) ──────────
        //
        // Controllers/AdminUsersController.cs — a plain ASP.NET Core Web API surface living
        // alongside this service's existing Razor Pages UI and Duende's own OIDC/OAuth protocol
        // endpoints. AddControllers() is new here; nothing else in this service used MVC
        // controllers before this task.
        _ = builder.Services.AddControllers();

        _ = builder.Services.AddScoped<IAdminUserService, AdminUserService>();

        // JWT bearer authentication against Duende IdentityServer — this service validates
        // tokens against ITSELF (Authority = IdentityServiceUrl, its own base URL), since it IS
        // the IdentityServer every other backend service already points its own AddJwtBearer
        // Authority at. Registered via the bare AddAuthentication() overload (no default-scheme
        // argument) — exactly the same pattern already used for Google external login above —
        // so this does NOT touch AddIdentity()'s own DefaultScheme (the cookie scheme Razor
        // Pages/MapRazorPages().RequireAuthorization() still relies on); AddJwtBearer registers
        // under its own default scheme name ("Bearer"), used explicitly (not ambiently) by the
        // "AdminOnly" policy below and by [Authorize(AuthenticationSchemes = ...)] on the
        // controller.
        //
        // NameClaimType/ValidTypes/ValidAudience mirror AuctionService.API's identical
        // AddJwtBearer wiring exactly (Config.UsernameClaimType = "username", "apexautobid",
        // "at+jwt"). No RoleClaimType override, for the same reason documented there: the
        // inbound "role" claim is auto-remapped to ClaimTypes.Role by JwtBearer's default legacy
        // claim mapping, which is exactly what RequireRole/User.IsInRole already check.
        _ = builder.Services.AddAuthentication()
            .AddJwtBearer(options =>
            {
                options.Authority = builder.Configuration["IdentityServiceUrl"];
                options.TokenValidationParameters.ValidAudience = Config.ApiScopeName;
                options.TokenValidationParameters.NameClaimType = Config.UsernameClaimType;
                options.TokenValidationParameters.ValidTypes = ["at+jwt"];

                // Containerized deployments (docker-compose/k8s) can't fetch the discovery
                // document from Authority: IdentityServiceUrl is the PUBLIC https://id.… domain,
                // whose dev certificate chains to the compose stack's throwaway CA — and this is
                // the one service that deliberately has no SSL_CERT_FILE (it must keep the public
                // root store to reach Cloudflare's Turnstile siteverify, docker-compose.yml).
                // JwtBearer:MetadataAddress lets those deployments point the metadata fetch at
                // the container's own plain-http loopback listener instead (the same self-issued
                // keys, no TLS involved). The loopback request carries no X-Forwarded-* headers,
                // so the discovery document it returns advertises the loopback issuer — hence the
                // explicit ValidIssuer: tokens are issued under the public IdentityServiceUrl
                // (JwtBearerHandler APPENDS metadata's issuer to ValidIssuers rather than
                // replacing ours, so both forms validate). Unset outside containers: local dev
                // and the test hosts keep fetching metadata from Authority unchanged.
                var metadataAddress = builder.Configuration["JwtBearer:MetadataAddress"];
                if (!string.IsNullOrEmpty(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                    // Required for an http:// MetadataAddress; safe here because the override is
                    // only ever an in-container loopback address, never a network hop.
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters.ValidIssuer = builder.Configuration["IdentityServiceUrl"];
                }
            });

        // "AdminOnly" — every AdminUsersController action requires this policy (Requirements.md
        // §10: "every admin endpoint returns 403 for non-admin callers"). AddAuthenticationSchemes
        // pins evaluation to the Bearer scheme specifically (not the ambient default/cookie
        // scheme) so this policy's outcome depends only on the presented JWT, regardless of any
        // unrelated browser cookie session. RequireAuthenticatedUser() is what actually produces
        // the 401-vs-403 split (decompile-confirmed in AuctionService.API's identical policy
        // comment: PolicyEvaluator.AuthorizeAsync challenges — 401 — when authentication itself
        // fails, and only forbids — 403 — once a principal exists but RequireRole fails).
        _ = builder.Services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole("admin"));

        // ── OpenAPI + Scalar for the admin API (Phase 11 Task 2.8) ───────────────────────
        //
        // AddOpenApi() only ever documents Controller/minimal-API endpoints (Razor Pages and
        // Duende's own protocol endpoints are never part of the generated document), so this
        // document is exactly, and only, AdminUsersController's operations.
        _ = builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
            options.AddOperationTransformer<AdminAuthorizeOperationTransformer>();
        });

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            _ = app.UseDeveloperExceptionPage();
        }

        // Phase 3 Task 17 — registered unconditionally (matches AuctionService.API's/
        // SearchService.API's "UseExceptionHandler first, wraps the whole pipeline" convention
        // exactly), even though in Development UseDeveloperExceptionPage() above — registered
        // first, so outermost — catches every exception before this middleware ever sees one:
        // that gives BOTH browser and JSON/API callers the full interactive diagnostic page in
        // Dev, which is richer than anything GlobalExceptionHandler's own Development branch
        // could offer for an HTML navigation (Pages/Error/Index.cshtml only knows how to render
        // Duende's OWN ErrorMessage shape, not a raw exception/stack trace). Left unconditional
        // anyway — rather than only registering it in the else branch — so Dev and Production
        // share one structurally identical wiring line, and a hypothetical future removal of
        // UseDeveloperExceptionPage() doesn't also require remembering to add this back.
        //
        // The "/Error" fallback path only ever matters for a request GlobalExceptionHandler
        // declined (an HTML-navigation request — see its own remarks) — it re-executes against
        // Duende's own existing, unmodified error page, decompile-confirmed to render gracefully
        // with no errorId/error context at all.
        _ = app.UseExceptionHandler("/Error");

        _ = app.UseStaticFiles();

        // Phase 3 Task 16.4 — placed before UseRouting()/UseIdentityServer() deliberately: the
        // GlobalLimiter above matches on raw HttpContext.Request.Path/Method, not endpoint
        // metadata, so it needs no routing to have run first, and rejecting an over-limit
        // request here means it never reaches routing, IdentityServer's endpoint dispatch, or
        // Razor Pages model binding at all — the cheapest possible place to turn it away.
        _ = app.UseRateLimiter();

        _ = app.UseRouting();
        _ = app.UseIdentityServer();
        _ = app.UseAuthorization();

        _ = app.MapRazorPages()
            .RequireAuthorization();

        // ── Health endpoints (Phase 3 Task 18) ────────────────────────────────────
        //
        // Anonymous per Requirements §13.4. .AllowAnonymous() is applied on both mappings even
        // though MapRazorPages() above's RequireAuthorization() is scoped to Razor Pages'
        // own endpoints, not a global AddAuthorization(options => options.FallbackPolicy = ...)
        // (this service sets none — confirmed by inspection) — so these two endpoints are not
        // actually ambient-policy-protected either way. Kept anyway, matching AuctionService.API's
        // and SearchService.API's identical MapHealthChecks(...).AllowAnonymous() calls exactly:
        // a defensive, explicit-is-better-than-implicit convention shared across every service
        // with this pattern, not something this specific pipeline strictly requires today — and
        // live-verified unauthenticated regardless (see this task's report). /health/live never
        // runs a check (Predicate =
        // _ => false) so it reflects only "is the process up". /health/ready only runs checks
        // tagged "ready" (PostgreSQL, registered above) — mirrors AuctionService.API's/
        // SearchService.API's identical Task 21/14 pattern, with PostgreSQL as Identity's one
        // and only readiness dependency (Requirements §13.4's table). Both are GET-only, and the
        // Task 16.4 rate limiter's GlobalLimiter only ever matches POST requests to /Account/
        // Login, /Account/Register, and /connect/token — any GET, these two included, always
        // falls through to RateLimitPartition.GetNoLimiter, so orchestrator probes are never
        // throttled (live-verified for this task, not assumed).
        _ = app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        _ = app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .AllowAnonymous();

        // ── Admin user-management API (Phase 11 Task 2) ──────────────────────────────
        //
        // MapControllers() serves AdminUsersController; each of its actions carries its own
        // [Authorize(AuthenticationSchemes = "Bearer", Policy = "AdminOnly")] (see the
        // controller), independent of MapRazorPages()'s own RequireAuthorization() above (a
        // Controller endpoint is never subject to that call — the two mapping calls are
        // unrelated authorization chains).
        _ = app.MapControllers();

        // ── OpenAPI document + Scalar UI for the admin API (Phase 11 Task 2.8) ───────────
        //
        // Mapped unconditionally, matching AuctionService.API's/BiddingService.API's own
        // standalone docs pages — not dev-only. MapOpenApi() serves the raw document at
        // /openapi/v1.json (proxied by the Gateway at /openapi/identity/v1.json — see
        // GatewayService's "openapi-identity" route/AddDocument("identity", ...) call);
        // MapScalarApiReference() serves the interactive Scalar UI at /scalar.
        //
        // AddAuthorizationCodeFlow wires Scalar's "Authorize" button to the "OAuth2" security
        // scheme (OAuth2SecuritySchemeTransformer) via the `scalar` client (Config.cs, which now
        // also lists this service's own https://localhost:5001/scalar redirect URI). Matches
        // AuctionService.API's identical wiring; WithRedirectUri/WithSelectedScopes must match
        // that client's RedirectUris/AllowedScopes entries exactly.
        var identityServiceUrl = app.Configuration["IdentityServiceUrl"];

        _ = app.MapOpenApi();
        _ = app.MapScalarApiReference(options =>
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
                        .WithRedirectUri("https://localhost:5001/scalar")
                        .WithSelectedScopes(["openid", "profile", "apexautobid"]);
                });
                options.AddPreferredSecuritySchemes("OAuth2");
            }
        });

        return app;
    }
}
