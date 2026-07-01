using System.Net;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests for the anonymous health endpoints (Phase 1 Task 21, Requirements
/// §13.4). GET /health/live never runs a check, so it should report 200 as soon as the
/// process is up. GET /health/ready fans out to the "ready"-tagged checks — PostgreSQL
/// (AspNetCore.HealthChecks.NpgSql) and RabbitMQ (MassTransit's own bus health check) —
/// both of which are backed by real Testcontainers in this test host.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class HealthCheckTests(CustomWebAppFactory factory)
{
    // ── 21.1  GET /health/live — always 200, no dependency checks ───────────────
    [Fact]
    public async Task HealthLive_Returns_Ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 21.2  GET /health/ready — PostgreSQL + RabbitMQ (MassTransit bus) ───────
    [Fact]
    public async Task HealthReady_Returns_Ok()
    {
        var client = factory.CreateClient();

        // The MassTransit bus can take a moment to report healthy right after the host
        // starts (broker connection handshake), so poll briefly instead of asserting once.
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }
}
