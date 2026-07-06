using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GatewayService.IntegrationTests;

/// <summary>
/// A real, Kestrel-hosted stand-in for the Auction/Search Services, bound to an OS-assigned
/// loopback port. YARP's forwarder makes an actual outbound HTTP call to a cluster
/// destination's <c>Address</c> for every proxied request — <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// normal in-memory <c>TestServer</c> transport is only reachable through that factory's own
/// <see cref="System.Net.Http.HttpMessageHandler"/>, never by another process/host dialing a
/// URL, so a genuine socket is required here (see this task's own framing). One stub instance
/// answers for BOTH the auction and search clusters (distinguished purely by request path, same
/// as the real services would be) — nothing here asserts cross-service isolation, so a single
/// process is simplest.
/// </summary>
internal sealed class StubDownstreamServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StubDownstreamServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// The stub's own real base address (e.g. <c>http://127.0.0.1:54321</c>) — fed into the
    /// gateway's <c>ReverseProxy:Clusters:*:Destinations:destination1:Address</c> configuration
    /// by <see cref="CustomWebAppFactory"/> so YARP forwards proxied requests here instead of
    /// the dev-only localhost:5054/5055 addresses in appsettings.Development.json.
    /// </summary>
    public string BaseUrl { get; }

    public static async Task<StubDownstreamServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // Binding port 0 asks the OS for any free loopback port — required so parallel test
        // runs (and this factory's own multiple stub instances, if ever added) never collide on
        // a fixed port the way a hardcoded dev port would.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Keep test output focused on xUnit's own results — this stub's request/response
        // logging adds nothing a test assertion doesn't already cover.
        builder.Logging.ClearProviders();

        var app = builder.Build();

        // ── Auction Service stand-in ──────────────────────────────────────────────
        app.MapGet("/api/auctions", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "auction-get";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });

        app.MapPost("/api/auctions", async context =>
        {
            // Reflects whether the Authorization header actually reached the stub — proves
            // YARP forwards it unchanged rather than stripping it, without needing to parse
            // the token's contents here.
            var authorizationHeaderPresent = context.Request.Headers.ContainsKey("Authorization");

            context.Response.Headers["X-Stub-Hit"] = "auction-post";
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(new
            {
                source = "auction-stub",
                method = "POST",
                authorizationHeaderPresent,
            });
        });

        // ── Search Service stand-in ───────────────────────────────────────────────
        app.MapGet("/api/search", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "search-get";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "search-stub", method = "GET" });
        });

        await app.StartAsync();

        // WebApplication.Urls reflects IServerAddressesFeature.Addresses, which Kestrel only
        // resolves to the actual bound port (replacing the ":0" placeholder) once the server has
        // started — reading it any earlier would still show the unbound "http://127.0.0.1:0".
        var baseUrl = app.Urls.First(u => u.StartsWith("http://127.0.0.1", StringComparison.Ordinal));

        return new StubDownstreamServer(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
