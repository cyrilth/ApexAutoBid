using System.Net;
using System.Net.Http.Json;
using AuctionService.Application.DTOs;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests for the AuctionsController write endpoints (Phase 1 Task 15),
/// running the full HTTP + MVC + EF Core pipeline against real containerized infrastructure.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class AuctionsControllerIntegrationTests(CustomWebAppFactory factory)
{
    private HttpClient CreateClient(string? asUser)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
        return client;
    }

    private async Task<Guid> GetAuctionIdOwnedByAsync(string seller)
    {
        var client = factory.CreateClient(); // GET is anonymous
        var auctions = await client.GetFromJsonAsync<List<AuctionDto>>(
            "api/auctions", TestContext.Current.CancellationToken);
        return auctions!.First(a => a.Seller == seller).Id;
    }

    // ── 15.1  CreateAuction — invalid DTO returns 400 ────────────────────────────
    [Fact]
    public async Task CreateAuction_WithInvalidDto_Returns400()
    {
        var client = CreateClient(asUser: "bob"); // authenticated, verified — reaches model validation
        // Empty body: required Make/Model/Color/Images are missing → model validation fails.
        var response = await client.PostAsJsonAsync(
            "api/auctions", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── 15.2  UpdateAuction — valid DTO and owner returns 200 ─────────────────────
    [Fact]
    public async Task UpdateAuction_WithValidDtoAndOwner_Returns200()
    {
        var id = await GetAuctionIdOwnedByAsync("bob");
        var client = CreateClient(asUser: "bob"); // the owner

        var response = await client.PutAsJsonAsync(
            $"api/auctions/{id}", new UpdateAuctionDto { Color = "Blue" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 15.3  UpdateAuction — valid DTO and non-owner returns 403 ─────────────────
    [Fact]
    public async Task UpdateAuction_WithValidDtoAndNonOwner_Returns403()
    {
        var id = await GetAuctionIdOwnedByAsync("bob");
        var client = CreateClient(asUser: "alice"); // NOT the owner

        var response = await client.PutAsJsonAsync(
            $"api/auctions/{id}", new UpdateAuctionDto { Color = "Blue" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
