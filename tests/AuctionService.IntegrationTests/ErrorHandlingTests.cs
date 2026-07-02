using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests for global error handling (Phase 1 Task 19) confirming that error
/// responses are served as RFC 7807 ProblemDetails (application/problem+json) on the real
/// HTTP + MVC pipeline.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class ErrorHandlingTests(CustomWebAppFactory factory)
{
    // ── 19.1  Model validation failure → 400 ProblemDetails ──────────────────────
    [Fact]
    public async Task InvalidCreate_Returns_ProblemDetailsContentType()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "bob");

        var response = await client.PostAsJsonAsync(
            "api/auctions", new { }, TestContext.Current.CancellationToken); // invalid: missing required fields

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
