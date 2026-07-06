using System.Net;
using Xunit;

namespace GatewayService.IntegrationTests;

/// <summary>
/// Proves the rate limiter's per-client-IP fixed-window partitioning (Requirements §3.5,
/// Program.cs's <c>GetClientIp</c>) genuinely isolates one caller's usage from another's — a gap
/// flagged at phase-end code review: every request through
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>'s in-memory
/// <c>TestServer</c> otherwise carries <c>Connection.RemoteIpAddress == null</c>, so the whole
/// suite would silently share Program.cs's "unknown" fallback partition and this partitioning
/// would never actually be exercised. Uses <see cref="PartitionIsolationWebAppFactory"/>'s
/// TEST-ONLY <see cref="TestClientIpStartupFilter"/> seam (see that type's own remarks) to assign
/// each request a simulated client IP via the <see cref="TestClientIpStartupFilter.HeaderName"/>
/// request header. Uses <c>GET api/version</c> (a gateway-only endpoint carrying the "general"
/// policy — same choice as <see cref="RateLimitingTests"/>) so these tests need no downstream stub.
/// </summary>
[Collection(PartitionIsolationApiCollection.Name)]
public class PartitionIsolationTests(PartitionIsolationWebAppFactory factory)
{
    private const string FirstClientIp = "10.0.0.1";
    private const string SecondClientIp = "10.0.0.2";

    [Fact]
    public async Task ExhaustingLimitForOneIp_LeavesADifferentIpUnaffected_WithinTheSameWindow()
    {
        var client = factory.CreateClient();

        // Exhaust 10.0.0.1's entire budget — every one of these must succeed.
        for (var i = 0; i < PartitionIsolationWebAppFactory.PermitLimit; i++)
        {
            using var response = await SendAsync(client, FirstClientIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 10.0.0.1 is now over budget for this fixed window.
        using var firstIpOverLimit = await SendAsync(client, FirstClientIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstIpOverLimit.StatusCode);

        // Immediately afterward, in the exact same window, a DIFFERENT client IP must be
        // completely unaffected — proving the two callers' usage is tracked in isolated
        // partitions rather than a single shared ("unknown") one.
        using var secondIpStillOk = await SendAsync(client, SecondClientIp);
        Assert.Equal(HttpStatusCode.OK, secondIpStillOk.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string simulatedClientIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/version");
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, simulatedClientIp);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
