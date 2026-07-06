using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace GatewayService.IntegrationTests;

/// <summary>
/// TEST-ONLY PIPELINE SEAM — never referenced from, nor added to, any production code path
/// (Program.cs is completely untouched by this type). Every request that travels through
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>'s in-memory
/// <c>TestServer</c> transport carries <c>HttpContext.Connection.RemoteIpAddress == null</c> — so
/// without this seam, every request in the whole test suite would share Program.cs's
/// <c>GetClientIp</c> "unknown" fallback partition, and the rate limiter's per-client-IP
/// partitioning (Requirements §3.5) would never actually be exercised.
/// <para>
/// Registered as an <see cref="IStartupFilter"/> (see <see cref="PartitionIsolationWebAppFactory"/>)
/// rather than a normal <c>app.Use(...)</c> call in Program.cs itself, because
/// <see cref="IStartupFilter"/> wraps the ENTIRE already-configured pipeline — including
/// <c>app.UseRateLimiter()</c> — guaranteeing this middleware runs first, before the rate limiter
/// ever reads <c>Connection.RemoteIpAddress</c>, regardless of registration order relative to
/// anything else.
/// </para>
/// <para>
/// Reads the caller-supplied <see cref="HeaderName"/> test header and, if present and a valid IP
/// address, overwrites <c>Connection.RemoteIpAddress</c> with it — letting a single test process
/// simulate multiple distinct client IPs against one shared rate-limiter partition map.
/// </para>
/// </summary>
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    /// <summary>
    /// The request header a test uses to select which simulated client IP a given request comes
    /// from — e.g. <c>"10.0.0.1"</c> vs <c>"10.0.0.2"</c>, to prove the rate limiter's fixed-window
    /// partitions are genuinely isolated per IP (Program.cs's <c>GetClientIp</c>).
    /// </summary>
    public const string HeaderName = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        applicationBuilder =>
        {
            applicationBuilder.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue) &&
                    IPAddress.TryParse(headerValue.ToString(), out var testClientIp))
                {
                    context.Connection.RemoteIpAddress = testClientIp;
                }

                await nextMiddleware();
            });

            // Runs the real, already-configured pipeline (UseRateLimiter, UseAuthentication,
            // MapReverseProxy, etc. from Program.cs) AFTER the IP-stamping middleware above.
            next(applicationBuilder);
        };
}
