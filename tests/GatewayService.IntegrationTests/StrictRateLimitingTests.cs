using System.Net;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Integration tests for the gateway's "strict" rate-limit policy (phase-end code review
/// follow-up — Docs/Tasks.md Phase 4 Task 8.1's mutating-route policy, previously untested:
/// <see cref="RateLimitingTests"/> only ever exercised "general"). Against
/// <see cref="StrictRateLimitedWebAppFactory"/>'s tiny, isolated 2-request/30s "Mutating" policy —
/// see that type's remarks for why it is its own dedicated factory instance.
/// <para>
/// Every request below is UNAUTHENTICATED on purpose. Program.cs wires rate limiting BEFORE
/// authentication (<c>app.UseRateLimiter()</c> precedes <c>app.UseAuthentication()</c>) precisely
/// so an over-limit caller is rejected as cheaply as possible, before ever reaching JWT parsing —
/// this suite proves that ordering end-to-end: requests within budget still reach
/// authentication and get its 401 (no token was presented), while the request that exceeds the
/// budget never reaches authentication at all and gets 429 instead.
/// </para>
/// </summary>
[Collection(StrictRateLimitedApiCollection.Name)]
public class StrictRateLimitingTests(StrictRateLimitedWebAppFactory factory)
{
    // ── "strict" policy crossover: under-limit -> 401 (auth), over-limit -> 429 (rate limiter) ──
    [Fact]
    public async Task PostAuctions_Unauthenticated_UnderLimitReturns401_ThenExactlyOverLimitReturns429()
    {
        var client = factory.CreateClient();

        // Consume exactly the configured budget. The rate limiter admits each of these, so they
        // fall through to JwtBearer's OnChallenge — no Authorization header was sent — and come
        // back 401, never 429, exactly like AuthenticationTests' equivalent no-token assertion.
        for (var i = 0; i < StrictRateLimitedWebAppFactory.PermitLimit; i++)
        {
            using var withinLimit = await client.PostAsync("api/auctions", content: null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, withinLimit.StatusCode);
        }

        // The next request is over budget for this fixed window — rejected by the rate limiter
        // itself, before authentication ever runs, so it is 429, not 401.
        using var overLimit = await client.PostAsync("api/auctions", content: null, TestContext.Current.CancellationToken);
        var body = await overLimit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
        Assert.Equal("application/problem+json", overLimit.Content.Headers.ContentType?.MediaType);
        Assert.True(overLimit.Headers.RetryAfter is not null);
        Assert.False(string.IsNullOrWhiteSpace(body));
    }
}
