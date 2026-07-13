using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Boots the real Gateway Service in-memory. Unlike AuctionService/SearchService's
/// <c>CustomWebAppFactory</c>, no Testcontainers-backed database/broker is needed — the gateway
/// itself has none (Requirements §13.4's "no downstream fan-out" — see its own
/// <c>AddHealthChecks()</c> comment). Its only two external dependencies are (1) the Auction/
/// Search/Identity/Bidding Service YARP cluster destinations, stood in for by a real,
/// loopback-socket <see cref="StubDownstreamServer"/> (see that type's remarks for why an
/// in-memory <c>TestServer</c> can't be used here), and (2) IdentityServer's JWKS/discovery
/// document for JwtBearer validation, replaced with a static in-memory configuration carrying
/// <see cref="TestJwt.SigningKey"/> (see <see cref="TestJwt"/>'s remarks) so no live
/// IdentityServer is required either.
/// </summary>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private StubDownstreamServer? _downstream;

    public async ValueTask InitializeAsync()
    {
        _downstream = await StubDownstreamServer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var downstream = _downstream
            ?? throw new InvalidOperationException(
                $"{nameof(StubDownstreamServer)} must be started (InitializeAsync) before the host is built.");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Redirects the auction/search/identity/bidding clusters (appsettings.
                // Development.json's localhost:5054/5055/5001/7003 dev addresses) at the real
                // stub socket instead — YARP route MATCHING (paths/methods/policies in
                // appsettings.json) is left completely untouched; only the cluster DESTINATION
                // address changes, exactly like a real deployment's environment-specific
                // override would. identity-cluster/bidding-cluster were added for Phase 11 Task
                // 7's admin-routing tests (AdminRoutingTests.cs).
                ["ReverseProxy:Clusters:auction-cluster:Destinations:destination1:Address"] = downstream.BaseUrl,
                ["ReverseProxy:Clusters:search-cluster:Destinations:destination1:Address"] = downstream.BaseUrl,
                ["ReverseProxy:Clusters:identity-cluster:Destinations:destination1:Address"] = downstream.BaseUrl,
                ["ReverseProxy:Clusters:bidding-cluster:Destinations:destination1:Address"] = downstream.BaseUrl,

                // Never dialed over the network in this test host (see the JwtBearerOptions
                // PostConfigure below) — kept as an obviously-fake placeholder purely so
                // Program.cs's Scalar OAuth2-flow wiring (gated on this value being non-blank)
                // still runs the same code path it would in every other environment.
                ["IdentityServiceUrl"] = "https://identityserver.invalid",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replaces JwtBearerOptions' signing-key SOURCE only — Program.cs's own Configure
            // callback (Authority, ValidAudience "apexautobid", NameClaimType "username",
            // ValidTypes ["at+jwt"], and critically the OnChallenge/OnForbidden ProblemDetails
            // events under test — Task 5.3) is left completely in place; see TestJwt's remarks
            // for why the "Bearer" scheme itself is never swapped out the way AuctionService/
            // SearchService's TestAuthHandler swaps out authentication entirely.
            //
            // Registered as a SECOND IPostConfigureOptions<JwtBearerOptions> for the "Bearer"
            // scheme (services.AddAuthentication().AddJwtBearer() in Program.cs already
            // registered the framework's own PostConfigureJwtBearerOptions) — explicitly setting
            // ConfigurationManager here (not just Configuration) guarantees this static,
            // no-network-call configuration wins regardless of PostConfigure ordering: the
            // framework's own PostConfigure would otherwise build a REAL, Authority-fetching
            // ConfigurationManager<OpenIdConnectConfiguration> the first time it observes
            // Configuration == null and ConfigurationManager == null, which happens before this
            // callback (registered later, via ConfigureTestServices) ever runs.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = false;

                var configuration = new OpenIdConnectConfiguration();
                configuration.SigningKeys.Add(TestJwt.SigningKey);

                options.Configuration = configuration;
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                // TestJwt-minted tokens carry no "iss" claim at all (see TestJwt's remarks) —
                // only signature/audience/type validation matter for these tests, so issuer
                // validation is turned off rather than faked with a placeholder issuer neither
                // side would otherwise care about.
                options.TokenValidationParameters.ValidateIssuer = false;
                options.TokenValidationParameters.IssuerSigningKey = TestJwt.SigningKey;
            });
        });
    }

    // xUnit v3's IAsyncLifetime inherits IAsyncDisposable, so teardown is a ValueTask-returning
    // DisposeAsync — mirrors AuctionService.IntegrationTests/CustomWebAppFactory.cs's identical
    // reasoning.
    public override async ValueTask DisposeAsync()
    {
        if (_downstream is not null)
        {
            await _downstream.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}

/// <summary>
/// Groups every test class that depends on <see cref="CustomWebAppFactory"/> into a single
/// xUnit collection so they share one factory instance (and one stub downstream server) instead
/// of each spinning up their own — mirrors AuctionServiceApiCollection/SearchServiceApiCollection's
/// identical rationale. The rate-limiting suite deliberately does NOT join this collection (see
/// <see cref="RateLimitedWebAppFactory"/>) — it needs its own tiny, isolated limiter config.
/// </summary>
[CollectionDefinition(Name)]
public class GatewayServiceApiCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "GatewayService.API";
}
