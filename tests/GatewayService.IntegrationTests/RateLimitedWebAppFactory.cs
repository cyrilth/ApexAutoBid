using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// A second, dedicated <see cref="WebApplicationFactory{TEntryPoint}"/> instance used ONLY by
/// <see cref="RateLimitingTests"/> (Task 8.2), configured with a tiny
/// <c>RateLimiting:General</c> permit limit so the test can trip it in a handful of requests
/// instead of the real, environment-agnostic default (100/60s from appsettings.json) — a fast,
/// non-flaky test rather than either waiting a full minute or firing 101 real requests.
/// </summary>
/// <remarks>
/// Deliberately its OWN factory/collection, not a member of <see cref="GatewayServiceApiCollection"/>:
/// <see cref="Microsoft.AspNetCore.RateLimiting.RateLimiterOptions"/>'s fixed-window partitions
/// (Program.cs's <c>GetClientIp</c>) live in the DI container for the lifetime of ONE app
/// instance — sharing a factory with every other test in this project would mean an unrelated
/// test's requests count against this test's tiny 3-request budget (or vice versa), and xUnit
/// does not guarantee execution order within a shared collection. A dedicated instance gives
/// this test class its own isolated limiter state no matter what else in the suite runs
/// concurrently.
/// </remarks>
public class RateLimitedWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The tiny permit limit configured below — shared with <see cref="RateLimitingTests"/> so
    /// the test doesn't hardcode a second copy of this number.
    /// </summary>
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A generous window (30s) relative to how long this in-memory test host takes
                // to fire a handful of HTTP requests — the fixed window must not roll over
                // mid-test (which would silently reset the counter and make the assertion
                // flaky), and no test here waits for the window to elapse either way.
                ["RateLimiting:General:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:General:WindowSeconds"] = "30",
            });
        });
    }
}

/// <summary>
/// Groups <see cref="RateLimitingTests"/> into its own xUnit collection so it never shares a
/// <see cref="RateLimitedWebAppFactory"/> instance (and therefore never shares rate-limiter
/// state) with any other test class — see <see cref="RateLimitedWebAppFactory"/>'s remarks.
/// </summary>
[CollectionDefinition(Name)]
public class RateLimitedApiCollection : ICollectionFixture<RateLimitedWebAppFactory>
{
    public const string Name = "GatewayService.API.RateLimited";
}
