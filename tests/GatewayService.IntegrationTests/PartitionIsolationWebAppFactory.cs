using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// A fourth, dedicated <see cref="WebApplicationFactory{TEntryPoint}"/> instance used ONLY by
/// <see cref="PartitionIsolationTests"/> (phase-end code review follow-up), pairing the same tiny
/// isolated "general" policy technique as <see cref="RateLimitedWebAppFactory"/> with the
/// <see cref="TestClientIpStartupFilter"/> TEST-ONLY seam — see that type's remarks for why it
/// exists at all: every request through a <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// in-memory <c>TestServer</c> otherwise carries a null <c>Connection.RemoteIpAddress</c>, so the
/// rate limiter's per-client-IP partitioning (Program.cs's <c>GetClientIp</c>) would never
/// actually be exercised by anything in this test project.
/// </summary>
/// <remarks>
/// Its own dedicated factory/collection for the same reason as
/// <see cref="RateLimitedWebAppFactory"/>/<see cref="StrictRateLimitedWebAppFactory"/>: shared
/// rate-limiter state across unrelated tests would make a tiny, deliberately-exhausted permit
/// budget flaky.
/// </remarks>
public class PartitionIsolationWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The tiny permit limit configured below — shared with <see cref="PartitionIsolationTests"/>
    /// so the test doesn't hardcode a second copy of this number.
    /// </summary>
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Same generous-window reasoning as RateLimitedWebAppFactory's identical choice.
                ["RateLimiting:General:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:General:WindowSeconds"] = "30",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // TEST-ONLY SEAM (see TestClientIpStartupFilter's own remarks) — added here, in the
            // TEST project's factory, never in Program.cs/production code. Wraps the pipeline so
            // it runs before app.UseRateLimiter(), letting each request in
            // PartitionIsolationTests pick its own simulated client IP via a request header.
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }
}

/// <summary>
/// Groups <see cref="PartitionIsolationTests"/> into its own xUnit collection so it never shares a
/// <see cref="PartitionIsolationWebAppFactory"/> instance (and therefore never shares rate-limiter
/// state) with any other test class — see <see cref="PartitionIsolationWebAppFactory"/>'s remarks.
/// </summary>
[CollectionDefinition(Name)]
public class PartitionIsolationApiCollection : ICollectionFixture<PartitionIsolationWebAppFactory>
{
    public const string Name = "GatewayService.API.PartitionIsolation";
}
