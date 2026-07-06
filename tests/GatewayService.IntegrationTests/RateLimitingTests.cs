using System.Net;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Integration tests for the gateway's rate limiting (Docs/Tasks.md Phase 4 Task 8.2), against
/// <see cref="RateLimitedWebAppFactory"/>'s tiny, isolated 3-request/30s "general" policy — see
/// that type's remarks for why it is a dedicated factory instance rather than a member of
/// <see cref="GatewayServiceApiCollection"/>. Uses <c>GET api/version</c> (a gateway-only
/// endpoint carrying the "general" policy — Program.cs) so these tests need no downstream stub
/// at all.
/// </summary>
[Collection(RateLimitedApiCollection.Name)]
public class RateLimitingTests(RateLimitedWebAppFactory factory)
{
    // ── 8.2  Exceeding the configured limit — 429 problem+json with Retry-After ──
    [Fact]
    public async Task ExceedingPermitLimit_Returns429WithProblemDetailsAndRetryAfter()
    {
        var client = factory.CreateClient();

        // Consume exactly the configured budget — every one of these must succeed.
        for (var i = 0; i < RateLimitedWebAppFactory.PermitLimit; i++)
        {
            using var withinLimit = await client.GetAsync("api/version", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, withinLimit.StatusCode);
        }

        // The next request is over budget for this fixed window.
        using var overLimit = await client.GetAsync("api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
        Assert.Equal("application/problem+json", overLimit.Content.Headers.ContentType?.MediaType);
        Assert.True(overLimit.Headers.RetryAfter is not null);
    }

    // ── Health endpoints are excluded from rate limiting entirely (Requirements §13.4) ──
    [Fact]
    public async Task HealthLive_NeverRateLimited_EvenWellOverThePermitLimit()
    {
        var client = factory.CreateClient();

        // Comfortably more requests than the tiny "general" budget above — /health/live carries
        // .DisableRateLimiting() (Program.cs), so none of these should ever be rejected.
        for (var i = 0; i < RateLimitedWebAppFactory.PermitLimit + 5; i++)
        {
            using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
