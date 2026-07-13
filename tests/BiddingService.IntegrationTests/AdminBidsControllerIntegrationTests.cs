using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BiddingService.Application.DTOs;
using BiddingService.Infrastructure.Data;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for <c>DELETE api/admin/bids/{id}</c> / <c>GET api/admin/bids/stats</c>
/// (Phase 11 Task 5.1/5.4), running the full HTTP + MVC + Mongo bus-outbox pipeline against real
/// containerized infrastructure — the admin-role authorization edge (401/403/200), the
/// current-high-bid recalculation, the real <c>BidRemoved</c> publish, and the append-only
/// <see cref="AuditEntryDocument"/> this writes.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class AdminBidsControllerIntegrationTests(CustomWebAppFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateClient(string? asUser, bool? emailVerified = null, string? role = null)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
        if (emailVerified is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailVerifiedHeader, emailVerified.Value ? "true" : "false");
        if (role is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return client;
    }

    private async Task<Guid> CreateLiveAuctionAsync(string seller)
    {
        var auctionId = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(
            new AuctionCreated(
                auctionId, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                seller, "", "Ford", "GT", 2020, "Red", 1000,
                "http://images.local/ford-gt.jpg", null, "Live", ReservePrice: 0, null, null),
            TestContext.Current.CancellationToken);

        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have stored a local projection");

        return auctionId;
    }

    private async Task<Guid> PlaceBidAsync(Guid auctionId, string bidder, int amount)
    {
        var client = CreateClient(asUser: bidder, emailVerified: true);
        var response = await client.PostAsJsonAsync(
            "api/bids", new PlaceBidDto { AuctionId = auctionId, Amount = amount },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bid = await response.Content.ReadFromJsonAsync<BidDto>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bid);
        return bid!.Id;
    }

    // ── 401 — anonymous caller ────────────────────────────────────────────────

    [Fact]
    public async Task RemoveBid_WhenAnonymous_Returns401()
    {
        var client = CreateClient(asUser: null);

        var response = await client.DeleteAsync($"api/admin/bids/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 9.1 — 403 for an authenticated caller without the admin role ────────────

    [Fact]
    public async Task RemoveBid_WhenCallerLacksTheAdminRole_Returns403()
    {
        var client = CreateClient(asUser: "carol", emailVerified: true); // no role header at all

        var response = await client.DeleteAsync($"api/admin/bids/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_WhenCallerLacksTheAdminRole_Returns403()
    {
        var client = CreateClient(asUser: "carol", emailVerified: true);

        var response = await client.GetAsync("api/admin/bids/stats", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 404 — no bid with the given id exists ────────────────────────────────────

    [Fact]
    public async Task RemoveBid_WhenTheBidDoesNotExist_Returns404()
    {
        var client = CreateClient(asUser: "admin", emailVerified: true, role: "admin");

        var response = await client.DeleteAsync($"api/admin/bids/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── 9.2 — recalculates the high bid, publishes BidRemoved, writes an AuditEntry ─────────

    [Fact]
    public async Task RemoveBid_WhenAdminRemovesTheHighestBid_RecalculatesCurrentHighAndPublishesBidRemoved()
    {
        var auctionId = await CreateLiveAuctionAsync("carol");
        await PlaceBidAsync(auctionId, "alice", 15000);
        var highBidId = await PlaceBidAsync(auctionId, "tom", 18000);

        await using var harness = await RabbitMqPublishHarness<BidRemoved>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        var client = CreateClient(asUser: "admin", emailVerified: true, role: "admin");
        var response = await client.DeleteAsync($"api/admin/bids/{highBidId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var published = await harness.WaitForMessageAsync(
            m => m.BidId == highBidId.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(auctionId.ToString(), published.AuctionId);
        Assert.Equal(15000, published.CurrentHighBid);

        // The removed bid must no longer count toward the auction's bid history.
        var bidsResponse = await client.GetAsync($"api/bids/{auctionId}", TestContext.Current.CancellationToken);
        var bids = await bidsResponse.Content.ReadFromJsonAsync<List<BidDto>>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bids);
        Assert.DoesNotContain(bids!, b => b.Id == highBidId);
        Assert.Single(bids!, b => b.Amount == 15000);

        // A subsequent bid must be validated against the recalculated ($15,000) high, not the
        // removed ($18,000) one.
        var nextBidId = await PlaceBidAsync(auctionId, "eve", 16000);
        var nextBid = (await (await client.GetAsync($"api/bids/{auctionId}", TestContext.Current.CancellationToken))
                .Content.ReadFromJsonAsync<List<BidDto>>(JsonOptions, TestContext.Current.CancellationToken))!
            .Single(b => b.Id == nextBidId);
        Assert.Equal("Accepted", nextBid.BidStatus);
    }

    // ── 9.6 — admin bid removal writes an append-only AuditEntry ────────────────

    [Fact]
    public async Task RemoveBid_WritesAnAuditEntryCapturingTheRemovedBid()
    {
        var auctionId = await CreateLiveAuctionAsync("carol");
        var bidId = await PlaceBidAsync(auctionId, "alice", 12000);

        var client = CreateClient(asUser: "admin-jane", emailVerified: true, role: "admin");
        var response = await client.DeleteAsync($"api/admin/bids/{bidId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        AuditEntryDocument? entry = null;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var collection = scope.ServiceProvider.GetRequiredService<IMongoCollection<AuditEntryDocument>>();
            entry = await (await collection.FindAsync(
                    d => d.Action == "BidRemoved" && d.EntityId == bidId.ToString(),
                    cancellationToken: TestContext.Current.CancellationToken))
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            if (entry is not null)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }

        Assert.NotNull(entry);
        Assert.Equal("admin-jane", entry!.Actor);
        Assert.True(entry.ActorIsAdmin);
        Assert.Equal("Bid", entry.EntityType);
        Assert.Contains("alice", entry.Data);
        Assert.Contains("12000", entry.Data);
        Assert.DoesNotContain("apexautobid.local", entry.Data); // never BidderEmail
    }

    // ── 5.4 — total bid count ─────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_AsAdmin_ReturnsTheTotalBidCountAndIncreasesAfterANewBid()
    {
        var client = CreateClient(asUser: "admin", emailVerified: true, role: "admin");

        var before = await client.GetFromJsonAsync<BidStatsDto>(
            "api/admin/bids/stats", JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(before);

        var auctionId = await CreateLiveAuctionAsync("carol");
        await PlaceBidAsync(auctionId, "alice", 12000);

        var after = await client.GetFromJsonAsync<BidStatsDto>(
            "api/admin/bids/stats", JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(after);

        Assert.Equal(before!.TotalBids + 1, after!.TotalBids);
    }
}
