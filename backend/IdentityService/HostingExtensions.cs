using System.Globalization;
using System.Threading.RateLimiting;
using Duende.IdentityModel;
using Duende.IdentityServer;
using IdentityService.Data;
using IdentityService.Handlers;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

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
            .AddInMemoryClients(Config.Clients)
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
                // IdentityService is reached directly today — no reverse proxy in front of it
                // yet. Once the Gateway (Phase 4/8) sits in front of it, every request will
                // appear to come from the gateway's own IP (sharing one bucket for every real
                // client) unless UseForwardedHeaders() is wired first to recover the real
                // client IP from X-Forwarded-For — deferred; out of this task's scope.
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

        return app;
    }
}
