using System.Linq;
using System.Net;
using System.Net.Http.Json;
using AuctionService.Application.DTOs;
using AuctionService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests verifying that mutating operations write an append-only
/// <c>AuditEntry</c> row in the SAME <c>SaveChanges</c> as the mutation (Phase 1
/// Task 20, Requirements §13.3).
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class AuditTrailTests(CustomWebAppFactory factory)
{
    // ── 20.1  CreateAuction — writes an "AuctionCreated" audit row ──────────────
    [Fact]
    public async Task CreateAuction_WritesAuditEntry()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "bob");

        // External image URL — the integration host has no MinIO, so a platform-hosted
        // URL would fail the HEAD size check; external URLs are exempt from that check.
        var dto = new CreateAuctionDto
        {
            Make = "Ford",
            Model = "GT",
            Color = "Red",
            Mileage = 1000,
            Year = 2020,
            ReservePrice = 20000,
            Images = [new ImageDto { Url = "https://example.com/car.jpg", SortOrder = 0 }],
            AuctionEnd = DateTime.UtcNow.AddDays(7),
        };

        var response = await client.PostAsJsonAsync(
            "api/auctions", dto, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auction = await response.Content.ReadFromJsonAsync<AuctionDto>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(auction);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        var entries = await dbContext.AuditEntries
            .Where(a => a.EntityId == auction!.Id.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("AuctionCreated", entry.Action);
        Assert.Equal("Auction", entry.EntityType);
        Assert.Equal("bob", entry.Actor);
    }
}
