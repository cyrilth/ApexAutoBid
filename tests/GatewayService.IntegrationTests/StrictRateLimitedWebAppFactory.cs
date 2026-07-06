using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// A third, dedicated <see cref="WebApplicationFactory{TEntryPoint}"/> instance used ONLY by
/// <see cref="StrictRateLimitingTests"/> (phase-end code review follow-up), configured with a
/// tiny <c>RateLimiting:Mutating</c> permit limit so the test can trip the "strict" policy — the
/// one Program.cs attaches to POST/PUT/DELETE <c>/api/auctions</c> and POST <c>/api/bids</c>
/// (appsettings.json's <c>ReverseProxy:Routes</c>) — in a handful of requests, rather than either
/// waiting a full minute or firing 11 real requests against the environment-agnostic default
/// (10/60s from appsettings.json).
/// </summary>
/// <remarks>
/// Deliberately its OWN factory/collection — not <see cref="RateLimitedWebAppFactory"/>'s, and not
/// <see cref="CustomWebAppFactory"/>'s either — for the exact same reason
/// <see cref="RateLimitedWebAppFactory"/>'s own remarks give for being separate from
/// <see cref="GatewayServiceApiCollection"/>: <see cref="Microsoft.AspNetCore.RateLimiting.RateLimiterOptions"/>'s
/// fixed-window partitions live in the DI container for the lifetime of one app instance, so
/// sharing a factory with any other test class here would mean an unrelated test's requests count
/// against this test's tiny budget (or vice versa).
/// </remarks>
public class StrictRateLimitedWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The tiny permit limit configured below — shared with <see cref="StrictRateLimitingTests"/>
    /// so the test doesn't hardcode a second copy of this number.
    /// </summary>
    public const int PermitLimit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A generous window (30s) relative to how long this in-memory test host takes to
                // fire a handful of HTTP requests — same reasoning as
                // RateLimitedWebAppFactory's identical WindowSeconds choice: the fixed window
                // must not roll over mid-test.
                ["RateLimiting:Mutating:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:Mutating:WindowSeconds"] = "30",
            });
        });

        // No StubDownstreamServer/cluster-destination override is needed here (unlike
        // CustomWebAppFactory): StrictRateLimitingTests only ever sends UNAUTHENTICATED requests,
        // so every one of them is rejected by either the rate limiter (429) or JwtBearer's
        // OnChallenge (401) before UseAuthorization()/MapReverseProxy() ever runs — the request
        // never reaches a cluster destination at all (see Program.cs's own "Middleware order"
        // comment above UseRateLimiter()).
    }
}

/// <summary>
/// Groups <see cref="StrictRateLimitingTests"/> into its own xUnit collection so it never shares a
/// <see cref="StrictRateLimitedWebAppFactory"/> instance (and therefore never shares rate-limiter
/// state) with any other test class — see <see cref="StrictRateLimitedWebAppFactory"/>'s remarks.
/// </summary>
[CollectionDefinition(Name)]
public class StrictRateLimitedApiCollection : ICollectionFixture<StrictRateLimitedWebAppFactory>
{
    public const string Name = "GatewayService.API.StrictRateLimited";
}
