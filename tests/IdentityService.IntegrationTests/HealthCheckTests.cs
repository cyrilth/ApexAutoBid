using System.Net;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Integration tests for the anonymous health endpoints (Phase 3 Task 18, Requirements §13.4).
/// GET /health/live never runs a check, so it should report 200 as soon as the process is up.
/// GET /health/ready fans out to the "ready"-tagged checks — PostgreSQL only for this service
/// (no RabbitMQ/MassTransit, no MongoDB, unlike Auction/Search/Bidding) — backed by the real
/// Testcontainers Postgres <see cref="CustomWebAppFactory"/> already boots for every test in
/// this collection. Mirrors AuctionService.IntegrationTests/HealthCheckTests.cs's shape (Phase 1
/// Task 21) with PostgreSQL as the sole readiness dependency; no MassTransit-bus-handshake delay
/// exists here, so — unlike that test — a single request is enough, no polling loop needed.
/// </summary>
[Collection(IdentityServiceApiCollection.Name)]
public class HealthCheckTests(CustomWebAppFactory factory)
{
    // ── 18.1  GET /health/live — always 200, no dependency checks ────────────────
    [Fact]
    public async Task HealthLive_Returns_Ok()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 18.2  GET /health/ready — PostgreSQL (the real Testcontainers instance) ──
    [Fact]
    public async Task HealthReady_Returns_Ok()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
