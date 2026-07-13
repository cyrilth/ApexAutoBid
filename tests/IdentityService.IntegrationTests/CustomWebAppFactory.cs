using Duende.IdentityServer.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Boots the real Identity Service in-memory against a throwaway PostgreSQL container (no
/// RabbitMQ — IdentityService doesn't use MassTransit). Mirrors
/// AuctionService.IntegrationTests/CustomWebAppFactory.cs's shape exactly, with two
/// IdentityService-specific additions documented on the members below: a test-only
/// Resource-Owner-Password-Credentials client (<see cref="ConfigureWebHost"/>) and a
/// redirected content root so Duende's Automatic Key Management doesn't write into the repo's
/// real backend/IdentityService/keys/ directory.
/// </summary>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    // Duende's Automatic Key Management (Config.cs / HostingExtensions.cs — unchanged in this
    // task) persists its RS256 signing key to <ContentRoot>/keys at runtime (see
    // backend/IdentityService/keys/ in local dev, gitignored). WebApplicationFactory's default
    // content-root discovery walks up from the test assembly looking for the SUT's own project
    // directory, which would resolve to the REAL backend/IdentityService/ folder — writing a
    // test-run signing key on top of (or alongside) the developer's actual dev key. Redirected
    // to a fresh temp directory per factory instance instead; nothing lands in the repo tree.
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("identityservice-inttests-").FullName;

    /// <summary>
    /// Client ID for the test-only Resource Owner Password Credentials client added in
    /// <see cref="ConfigureWebHost"/> — see that method's remarks for why ROPC, not the
    /// browser-interactive authorization-code+PKCE `webapp` client, is used to obtain a
    /// user-bound token non-interactively in these tests.
    /// </summary>
    public const string TestClientId = "integration-test-ropc";
    public const string TestClientSecret = "integration-test-ropc-secret";

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(_contentRoot);

        // Relies on WebApplicationFactory's default Development environment (it calls
        // UseEnvironment(Development) unconditionally): that's what makes Program.cs's
        // IsDevelopment()-gated SeedData run against the fresh Testcontainers database,
        // seeding the users these tests authenticate as. Don't override the environment here.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),

                // UseContentRoot(_contentRoot) above points WebApplicationBuilder's JSON config
                // providers (appsettings.json / appsettings.Development.json — both optional, so
                // this fails silently, not loudly) at a directory that has neither file, so
                // nothing from either file is actually loaded in this test host — this in-memory
                // override is standing in for BOTH, not just the Smtp section, but the DB
                // connection string above is the only other setting anything here depends on.
                // A syntactically valid but unreachable host is enough: Phase 3 Task 14's
                // SmtpOptions only needs to pass ValidateOnStart's DataAnnotations checks at
                // boot — no test in this project exercises Register/SmtpEmailSender, so nothing
                // here ever actually dials out to it.
                ["Smtp:Host"] = "smtp.invalid",
                ["Smtp:FromAddress"] = "noreply@apexautobid.local",
                ["Smtp:FromName"] = "ApexAutoBid (Integration Tests)",

                // Same reasoning as Smtp above — Phase 3 Task 16's TurnstileOptions only needs
                // to pass ValidateOnStart at boot; no test in this project exercises
                // Register/TurnstileValidator, so Cloudflare's real always-pass test keys aren't
                // even necessary here (any non-empty values satisfy [Required]).
                ["Turnstile:SiteKey"] = "1x00000000000000000000AA",
                ["Turnstile:SecretKey"] = "1x0000000000000000000000000000000AA",

                // Phase 11 Task 2 — the admin API's JwtBearer Authority (HostingExtensions.cs)
                // is this SAME service's own base URL, since it validates tokens it issued
                // itself. MUST be "http://localhost" — exactly WebApplicationFactory's own
                // default HttpClient.BaseAddress (CreateClient()) — NOT the real dev value
                // (https://localhost:5001): Duende dynamically derives the `iss` claim from
                // each individual request's own Host header/scheme (no fixed issuer setting
                // exists), so the token-request call (RequestPasswordGrantTokenAsync, a relative
                // "/connect/token" POST resolved against that same default BaseAddress) and the
                // discovery/JWKS backchannel call the PostConfigure<JwtBearerOptions> handler
                // below redirects into this SAME TestServer must present as the SAME host for
                // Duende to compute the SAME issuer value both times — otherwise JwtBearer
                // rejects a genuinely valid token with "the issuer '...' is invalid" (verified
                // live while building this test: https://localhost:5001 produced exactly that
                // mismatch, since the metadata fetch and the token request otherwise disagreed
                // on scheme/port).
                ["IdentityServiceUrl"] = "http://localhost",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Test-only Resource Owner Password Credentials (ROPC) client. The `webapp` client
            // registered in the real Config.cs is authorization-code+PKCE — browser-interactive
            // by design (login page redirect, antiforgery token, cookies) — which is exactly
            // right for production but not worth reimplementing headlessly here (that's the
            // established Postman/curl PKCE recipe used for manual/live verification in Tasks
            // 3-7, not something to re-drive through WebApplicationFactory's TestServer).
            //
            // The honest, minimal-footprint alternative: IdentityService.csproj already calls
            // .AddAspNetIdentity<ApplicationUser>() in HostingExtensions.cs (unchanged in this
            // task), which — per Duende.IdentityServer.AspNetIdentity's own AddAspNetIdentity
            // extension (verified via decompilation, not assumed) — already registers a REAL,
            // production IResourceOwnerPasswordValidator backed by
            // SignInManager.CheckPasswordSignInAsync. The ONLY thing missing for the password
            // grant to work is a CLIENT allowed to request it — that's what's added here, as a
            // test-only override, so this test project needs zero changes to Config.cs,
            // HostingExtensions.cs, or any appsettings file. Every claim (username, email,
            // email_verified, role) and the "apexautobid" audience still flow through the exact
            // real production Config.ApiResources / ProfileService wiring — only the CLIENT's
            // allowed grant type is test-only.
            //
            // AddInMemoryClients (Duende.IdentityServer, verified via decompilation) does a
            // plain services.AddSingleton(clients) — registering a SECOND IEnumerable<Client>
            // here, after the app's own ConfigureServices already ran, wins over the first for
            // InMemoryClientStore's single-instance constructor injection. This replaces the
            // "webapp" client for the test host entirely (fine — no test here needs it) while
            // leaving the app's IClientStore registration untouched, so Duende's
            // ValidatingClientStore decorator (client-configuration validation on every lookup)
            // still applies exactly as in production.
            var testClients = new List<Client>
            {
                new()
                {
                    ClientId = TestClientId,
                    ClientSecrets = { new Secret(TestClientSecret.Sha256()) },
                    AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                    AccessTokenType = AccessTokenType.Jwt,
                    AllowedScopes = { "openid", "profile", "apexautobid" },
                },
            };

            services.AddSingleton<IEnumerable<Client>>(testClients);

            // Phase 11 Task 2 — AdminUsersController's "AdminOnly" policy runs the REAL
            // JwtBearer handler (HostingExtensions.cs), whose Authority (IdentityServiceUrl,
            // just configured above) is this SAME service's own base URL — this service
            // validates tokens it issued itself. In production that Authority is a real,
            // reachable HTTP(S) endpoint; here, WebApplicationFactory's TestServer is in-memory
            // only and never actually listens on that address. PostConfigure swaps in a
            // DelegatingHandler (DeferredTestServerHandler, below) that redirects JwtBearer's
            // OIDC discovery/JWKS backchannel calls straight into THIS SAME TestServer's
            // in-memory pipeline instead of attempting a real socket connection — the handler
            // resolves `Server` lazily (only on the first actual backchannel call, well after
            // WebApplicationFactory has finished building/starting the host) since `Server`
            // itself is not yet available while ConfigureWebHost is still running.
            //
            // Configure (NOT PostConfigure): the framework's OWN JwtBearerPostConfigureOptions
            // (registered internally by AddJwtBearer) validates RequireHttpsMetadata as an
            // IPostConfigureOptions<JwtBearerOptions> specifically so it runs AFTER every
            // Configure-time customization has merged — meaning a PostConfigure call here would
            // run too LATE to relax RequireHttpsMetadata before that framework check throws
            // (verified live while building this test). Configure<T> runs before ALL
            // PostConfigure<T> callbacks regardless of registration order, which is what's needed
            // here.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.BackchannelHttpHandler = new DeferredTestServerHandler(() => Server);
                // IdentityServiceUrl above is "http://localhost" (not https) for this test host
                // specifically — see that config value's own remarks — so the app's own
                // (production-appropriate) RequireHttpsMetadata=true default is relaxed here,
                // test-only, to match.
                options.RequireHttpsMetadata = false;
            });
        });
    }

    /// <summary>
    /// Lazily forwards HTTP calls into a <see cref="TestServer"/>'s own in-memory handler,
    /// resolved on first use rather than at construction time — see the
    /// <c>PostConfigure&lt;JwtBearerOptions&gt;</c> call above for why this indirection is
    /// needed (the real <see cref="TestServer"/> instance doesn't exist yet while
    /// <see cref="ConfigureWebHost"/> is still building the host).
    /// </summary>
    private sealed class DeferredTestServerHandler(Func<TestServer> serverAccessor) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InnerHandler ??= serverAccessor().CreateHandler();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    // xUnit v3's IAsyncLifetime inherits IAsyncDisposable, so teardown is a ValueTask-returning
    // DisposeAsync — mirrors AuctionService.IntegrationTests/CustomWebAppFactory.cs's identical
    // reasoning. Also removes the redirected content-root temp directory (including whatever
    // Duende's key management wrote there) so repeated local test runs don't accumulate them.
    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();

        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort cleanup only — a locked file (IOException) or a permission/read-only
            // hiccup (UnauthorizedAccessException) here must not fail the test run.
        }
    }
}

/// <summary>
/// Groups every test class that depends on <see cref="CustomWebAppFactory"/> into a single
/// xUnit collection so they share one factory instance instead of each spinning up its own
/// PostgreSQL container — mirrors AuctionServiceApiCollection's identical rationale.
/// </summary>
[CollectionDefinition(Name)]
public class IdentityServiceApiCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "IdentityService.API";
}
