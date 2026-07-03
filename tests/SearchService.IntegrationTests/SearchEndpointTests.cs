using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Application.DTOs;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET api/search</c> (Phase 2 Task 10.4), seeding directly via
/// <see cref="IItemRepository.UpsertAsync"/> (synchronous — no bus round-trip needed for this
/// area) and asserting through the real HTTP + Mongo <c>PagedSearch</c> pipeline, including
/// the real text index <c>DbInitializer</c> creates at startup.
/// </summary>
[Collection(SearchServiceApiCollection.Name)]
public class SearchEndpointTests(CustomWebAppFactory factory)
{
    // ASP.NET Core serializes camelCase by default; GetFromJsonAsync's plain defaults are
    // case-sensitive PascalCase-only, so every property would silently bind to null/0
    // without this (same pitfall documented on AuctionServiceHttpClient).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A unique, invented word per test method instance (xUnit constructs a fresh instance of
    // this class per [Fact]) so a shared Mongo instance can't leak state between test methods
    // or between this class and the consumer test classes (whose data uses real-looking
    // makes like "Ford"/"Chevrolet").
    private readonly string _makeMarker = $"Zqxvarq{Guid.NewGuid():N}";
    private readonly string _sellerA = $"seller-a-{Guid.NewGuid():N}";
    private readonly string _sellerB = $"seller-b-{Guid.NewGuid():N}";

    private Item BuildItem(string model, string seller, string status, TimeSpan auctionEndOffset, int? soldAmount = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            AuctionEnd = DateTime.UtcNow.Add(auctionEndOffset),
            Seller = seller,
            Make = _makeMarker,
            Model = model,
            Year = 2022,
            Color = "Black",
            Mileage = 500,
            ImageUrl = "http://images.local/x.jpg",
            Status = status,
            ReservePrice = 5000,
            SoldAmount = soldAmount
        };

    // Alpha: Live, ending in 10h — Live but NOT ending-soon (outside the 6h window).
    // Bravo: Live, ending in 30m — Live AND ending-soon.
    // Charlie: Finished, ended 1h ago — finished.
    // Delta: Live, ending in 8h, DIFFERENT seller — Live but not ending-soon; seller isolation.
    // Echo: Status STILL "Live" but AuctionEnd 2h in the PAST — the "ended but the
    // finalizing event hasn't landed in this index yet" scenario ItemFilterBy.Finished's OR
    // branch (AuctionEnd<=now || Status!="Live") exists for; the only branch of that OR with
    // no prior end-to-end coverage.
    private async Task<(Item Alpha, Item Bravo, Item Charlie, Item Delta, Item Echo)> SeedStandardDatasetAsync()
    {
        var alpha = BuildItem("Alpha", _sellerA, "Live", TimeSpan.FromHours(10));
        var bravo = BuildItem("Bravo", _sellerA, "Live", TimeSpan.FromMinutes(30));
        var charlie = BuildItem("Charlie", _sellerA, "Finished", TimeSpan.FromHours(-1), soldAmount: 9000);
        var delta = BuildItem("Delta", _sellerB, "Live", TimeSpan.FromHours(8));
        var echo = BuildItem("Echo", _sellerA, "Live", TimeSpan.FromHours(-2));

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        foreach (var item in new[] { alpha, bravo, charlie, delta, echo })
            await repository.UpsertAsync(item, TestContext.Current.CancellationToken);

        return (alpha, bravo, charlie, delta, echo);
    }

    private async Task<SearchResultDto> SearchAsync(string query)
    {
        var client = factory.CreateClient();
        var result = await client.GetFromJsonAsync<SearchResultDto>(
            $"api/search?{query}", JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public async Task Search_BySearchTerm_MatchesTheRealTextIndex()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&pageSize=10");

        var ids = result.Results.Select(r => r.Id).ToHashSet();
        Assert.Contains(alpha.Id, ids);
        Assert.Contains(bravo.Id, ids);
        Assert.Contains(charlie.Id, ids);
        Assert.Contains(delta.Id, ids);
        Assert.Contains(echo.Id, ids);
        Assert.All(result.Results, r => Assert.Equal(_makeMarker, r.Make));
    }

    [Fact]
    public async Task Search_BySeller_ReturnsOnlyThatSellersItems()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&seller={_sellerA}&pageSize=10");

        var ids = result.Results.Select(r => r.Id).ToHashSet();
        Assert.Contains(alpha.Id, ids);
        Assert.Contains(bravo.Id, ids);
        Assert.Contains(charlie.Id, ids);
        Assert.Contains(echo.Id, ids);
        Assert.DoesNotContain(delta.Id, ids); // different seller
        Assert.All(result.Results, r => Assert.Equal(_sellerA, r.Seller));
    }

    [Fact]
    public async Task Search_FilterByLive_ReturnsOnlyLiveNotYetEndedItems()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&filterBy=live&pageSize=10");

        var ids = result.Results.Select(r => r.Id).ToHashSet();
        Assert.Contains(alpha.Id, ids);
        Assert.Contains(bravo.Id, ids);
        Assert.Contains(delta.Id, ids);
        Assert.DoesNotContain(charlie.Id, ids); // Finished
        Assert.DoesNotContain(echo.Id, ids); // Status is still "Live" but AuctionEnd is in the past
    }

    [Fact]
    public async Task Search_FilterByFinished_ReturnsOnlyFinishedOrEndedItems()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&filterBy=finished&pageSize=10");

        var ids = result.Results.Select(r => r.Id).ToHashSet();
        Assert.Contains(charlie.Id, ids);
        Assert.Contains(echo.Id, ids); // stale-Live: AuctionEnd<=now catches it via the OR
        Assert.DoesNotContain(alpha.Id, ids);
        Assert.DoesNotContain(bravo.Id, ids);
        Assert.DoesNotContain(delta.Id, ids);
        var charlieDto = result.Results.Single(r => r.Id == charlie.Id);
        Assert.Equal("Finished", charlieDto.Status);
        Assert.Equal(9000, charlieDto.SoldAmount);
        // Echo proves the OR's SECOND clause is unnecessary for this row — Status is still
        // "Live" (the first clause, AuctionEnd<=now, is what actually matches it).
        var echoDto = result.Results.Single(r => r.Id == echo.Id);
        Assert.Equal("Live", echoDto.Status);
    }

    [Fact]
    public async Task Search_FilterByEndingSoon_ReturnsOnlyItemsWithinTheWindow()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&filterBy=endingSoon&pageSize=10");

        var ids = result.Results.Select(r => r.Id).ToHashSet();
        Assert.Contains(bravo.Id, ids); // +30m — inside the 6h window
        Assert.DoesNotContain(alpha.Id, ids);   // +10h — outside
        Assert.DoesNotContain(delta.Id, ids);   // +8h — outside
        Assert.DoesNotContain(charlie.Id, ids); // already ended
        Assert.DoesNotContain(echo.Id, ids);    // already ended (and not "ending", already past)
    }

    [Fact]
    public async Task Search_OrderByMake_SortsByModelWhenMakeIsIdentical()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        var result = await SearchAsync($"searchTerm={_makeMarker}&orderBy=make&pageSize=10");

        // Every seeded item shares the same Make (the marker), so Model is the effective
        // tiebreaker — Alpha < Bravo < Charlie < Delta < Echo alphabetically.
        var ids = result.Results.Select(r => r.Id).ToList();
        var expectedOrder = new[] { alpha.Id, bravo.Id, charlie.Id, delta.Id, echo.Id };
        Assert.Equal(expectedOrder, ids);
    }

    [Fact]
    public async Task Search_Paging_ReturnsCorrectSliceAndMetadata()
    {
        var (alpha, bravo, charlie, delta, echo) = await SeedStandardDatasetAsync();

        // Default orderBy (endingSoon, AuctionEnd ascending): Echo(-2h) < Charlie(-1h) <
        // Bravo(+30m) < Delta(+8h) < Alpha(+10h). 5 items, pageSize 2 -> 3 pages (2, 2, 1).
        var page1 = await SearchAsync($"searchTerm={_makeMarker}&pageSize=2&pageNumber=1");
        var page2 = await SearchAsync($"searchTerm={_makeMarker}&pageSize=2&pageNumber=2");
        var page3 = await SearchAsync($"searchTerm={_makeMarker}&pageSize=2&pageNumber=3");

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.PageCount);
        Assert.Equal([echo.Id, charlie.Id], page1.Results.Select(r => r.Id).ToList());

        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(3, page2.PageCount);
        Assert.Equal([bravo.Id, delta.Id], page2.Results.Select(r => r.Id).ToList());

        Assert.Equal(5, page3.TotalCount);
        Assert.Equal(3, page3.PageCount);
        Assert.Equal([alpha.Id], page3.Results.Select(r => r.Id).ToList());
    }

    [Fact]
    public async Task Search_WithInvalidOrderBy_Returns400WithProblemDetails()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "api/search?orderBy=bogus", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // The wire-level ProblemDetails serialization (media type + shape) is only verifiable
        // at this HTTP integration seam — unit tests only ever see the in-memory ObjectResult.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal("Invalid orderBy", problem?.Title);
    }

    // Minimal shape for deserializing the ProblemDetails JSON body — avoids pulling in
    // Microsoft.AspNetCore.Mvc's ProblemDetails type just for its Title property.
    private sealed class ProblemDetailsBody
    {
        public string? Title { get; init; }
    }
}
