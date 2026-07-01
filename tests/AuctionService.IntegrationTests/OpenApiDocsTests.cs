using System.Net;
using System.Text.Json;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Verifies the OpenAPI document and Scalar UI are served and that the Bearer security scheme
/// (Task 16) is present and applied to the protected write endpoints but not the anonymous GETs.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class OpenApiDocsTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task OpenApiDocument_IsServed_WithBearerSecurityScheme()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Bearer scheme is declared in components.
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        // A protected endpoint (POST /api/auctions) carries a security requirement; the
        // collection GET does not.
        var auctionsPath = root.GetProperty("paths").GetProperty("/api/auctions");
        Assert.True(auctionsPath.GetProperty("post").TryGetProperty("security", out _),
            "POST /api/auctions should have a security requirement");
        Assert.False(auctionsPath.GetProperty("get").TryGetProperty("security", out _),
            "GET /api/auctions should not have a security requirement");
    }

    [Fact]
    public async Task ScalarUi_IsServed_AtScalar()
    {
        var client = factory.CreateClient(); // HttpClient follows redirects by default

        var response = await client.GetAsync("scalar");

        Assert.True(response.IsSuccessStatusCode, $"GET /scalar returned {(int)response.StatusCode}");
    }
}
