using System.Net;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for the anonymous health endpoints (Phase 5 Task 22, Requirements §13.4).
/// GET /health/live never runs a check, so it should report 200 as soon as the process is up.
/// GET /health/ready fans out to the "ready"-tagged checks — MongoDB
/// (AspNetCore.HealthChecks.MongoDb) and RabbitMQ (MassTransit's own bus health check) — both
/// backed by real Testcontainers in this test host. Mirrors
/// AuctionService.IntegrationTests/SearchService.IntegrationTests' identical
/// <c>HealthCheckTests</c>.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class HealthCheckTests(CustomWebAppFactory factory)
{
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

        // The MassTransit bus can take a moment to report healthy right after the host starts
        // (broker connection handshake), so poll briefly instead of asserting once.
        HttpResponseMessage? response = null;
        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                response?.Dispose();
                response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            }

            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        }
        finally
        {
            response?.Dispose();
        }
    }
}
