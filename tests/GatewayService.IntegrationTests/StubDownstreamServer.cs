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
/// answers for the auction, search, identity, AND bidding clusters (distinguished purely by
/// request path, same as the real services would be) — nothing here asserts cross-service
/// isolation, so a single process is simplest. Identity/bidding admin routes were added for
/// Phase 11 Task 7's admin routing tests.
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

        // GET api/auctions/duration-limits — Phase 11 Task 3.8's new public, anonymous
        // create-form limits endpoint; proves it's already reachable through the EXISTING
        // "auctions-read-catchall" route (no gateway config change needed for it, Task 7).
        app.MapGet("/api/auctions/duration-limits", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "auction-duration-limits";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });

        // GET api/banners — Phase 11 Task 3.5/7's new public, anonymous banner list.
        app.MapGet("/api/banners", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "banners-get";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });

        // ── Search Service stand-in ───────────────────────────────────────────────
        app.MapGet("/api/search", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "search-get";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "search-stub", method = "GET" });
        });

        // ── Identity Service admin API stand-in (Phase 11 Task 7) ────────────────
        app.MapGet("/api/admin/users", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "identity-admin-users";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "identity-stub", method = "GET" });
        });

        // ── Auction Service admin API stand-in (Phase 11 Task 7) ─────────────────
        app.MapGet("/api/admin/auctions", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "auction-admin-auctions";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });
        app.MapGet("/api/admin/banners", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "auction-admin-banners";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });
        app.MapGet("/api/admin/settings/duration", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "auction-admin-settings";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "auction-stub", method = "GET" });
        });

        // ── Bidding Service admin API stand-in (Phase 11 Task 7) ─────────────────
        app.MapGet("/api/admin/bids", async context =>
        {
            context.Response.Headers["X-Stub-Hit"] = "bidding-admin-bids";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { source = "bidding-stub", method = "GET" });
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
