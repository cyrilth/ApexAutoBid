using System.Net;
using System.Text.Json;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Cheap smoke coverage for the gateway's own non-proxied endpoints (Docs/Tasks.md Phase 4
/// Tasks 9, 11) — mirrors AuctionService/SearchService.IntegrationTests' HealthCheckTests.cs.
/// Neither endpoint fans out to a dependency (Requirements §13.4's "no downstream fan-out" for
/// the gateway specifically), so no polling is needed the way AuctionService's /health/ready
/// test polls for MassTransit's bus health check.
/// </summary>
[Collection(GatewayServiceApiCollection.Name)]
public class VersionAndHealthTests(CustomWebAppFactory factory)
{
    // ── 9  GET api/version — anonymous, returns the platform version ────────────
    [Fact]
    public async Task GetVersion_Returns200WithNonEmptyVersionString()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("api/version", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var version = doc.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    // ── 11  Health endpoints — gateway-only, no dependency fan-out ───────────────
    [Fact]
    public async Task HealthLive_Returns_Ok()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns_Ok()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
