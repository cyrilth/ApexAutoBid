using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BiddingService.Application.DTOs;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BiddingService.IntegrationTests;

/// <summary>
/// Integration tests for <c>POST/GET api/bids</c> (Phase 5 Tasks 16.1/16.2), running the full
/// HTTP + MVC + Mongo bus-outbox pipeline against real containerized infrastructure. Targets
/// pre-seeded auctions from <c>DbInitializer.SeedDataAsync</c> (Requirements §8.2/§8.3) — the
/// same fixed literal ids that service's own remarks document — so no test here needs to wait
/// on an <c>AuctionCreated</c> consume first.
/// </summary>
[Collection(BiddingServiceApiCollection.Name)]
public class PlaceBidEndpointTests(CustomWebAppFactory factory)
{
    // ASP.NET Core serializes camelCase by default; ReadFromJsonAsync's plain defaults are
    // case-sensitive PascalCase-only, so every property would silently bind to null/0 without
    // this (mirrors SearchEndpointTests' identical convention).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Fixed seed ids (Requirements §8.2/§8.3 — DbInitializer.SeedDataAsync's own literals,
    // reproduced here so this class doesn't need its own AuctionCreated round-trip).
    private static readonly Guid BmwX1Id = Guid.Parse("019f34ea-a60a-7142-a876-f92dbd679148"); // alice, reserve 20000, no seed bids
    private static readonly Guid FerrariF430Id = Guid.Parse("019f34ea-a60a-7e7c-863d-b313a76f80ec"); // alice, reserve 150000, no seed bids
    private static readonly Guid AudiR8Id = Guid.Parse("019f34ea-a60a-70fd-b40f-e6d7c2090457"); // bob, reserve 0, no seed bids
    // bob, reserve 20000; seed bids: alice $15,000 (older), tom $18,000 (newer) — both AcceptedBelowReserve.
    private static readonly Guid FordGtId = Guid.Parse("019f34ea-a5dc-756c-923d-a43f3e66c6af");

    private HttpClient CreateClient(string? asUser, bool? emailVerified = null)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
        if (emailVerified is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailVerifiedHeader, emailVerified.Value ? "true" : "false");
        return client;
    }

    // ── 16.1  POST api/bids — valid bid publishes BidPlaced ──────────────────────

    [Fact]
    public async Task PlaceBid_WithValidBid_Returns200AndPublishesBidPlaced()
    {
        await using var harness = await RabbitMqPublishHarness<BidPlaced>.StartAsync(
            factory.RabbitMqHost, factory.RabbitMqPort,
            CustomWebAppFactory.RabbitMqUsername, CustomWebAppFactory.RabbitMqPassword,
            TestContext.Current.CancellationToken);

        var client = CreateClient(asUser: "bob", emailVerified: true); // bob is NOT BmwX1's seller (alice)
        var dto = new PlaceBidDto { AuctionId = BmwX1Id, Amount = 25000 };

        var response = await client.PostAsJsonAsync("api/bids", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bid = await response.Content.ReadFromJsonAsync<BidDto>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bid);
        Assert.Equal("Accepted", bid!.BidStatus);
        Assert.Equal(BmwX1Id, bid.AuctionId);
        Assert.Equal("bob", bid.Bidder);
        Assert.Equal(25000, bid.Amount);

        var published = await harness.WaitForMessageAsync(
            m => m.AuctionId == BmwX1Id.ToString() && m.Amount == 25000,
            TestContext.Current.CancellationToken);
        Assert.Equal("bob", published.Bidder);
        Assert.Equal("Accepted", published.BidStatus);
    }

    // ── 16.2  POST api/bids — unauthenticated returns 401 ────────────────────────

    [Fact]
    public async Task PlaceBid_WhenAnonymous_Returns401()
    {
        var client = CreateClient(asUser: null);
        var dto = new PlaceBidDto { AuctionId = AudiR8Id, Amount = 1000 };

        var response = await client.PostAsJsonAsync("api/bids", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Extra: 403 when email_verified is false ──────────────────────────────────

    [Fact]
    public async Task PlaceBid_WhenEmailNotVerified_Returns403()
    {
        var client = CreateClient(asUser: "carol", emailVerified: false);
        var dto = new PlaceBidDto { AuctionId = FerrariF430Id, Amount = 160000 };

        var response = await client.PostAsJsonAsync("api/bids", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Extra: GET api/bids/{auctionId} returns seeded-shape DTOs, no bidderEmail ─

    [Fact]
    public async Task GetBidsForAuction_ReturnsSeededBidsNewestFirstWithoutBidderEmail()
    {
        var client = factory.CreateClient(); // anonymous — GET requires no auth at all

        var response = await client.GetAsync($"api/bids/{FordGtId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("bidderEmail", body, StringComparison.OrdinalIgnoreCase);

        var bids = await response.Content.ReadFromJsonAsync<List<BidDto>>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bids);
        Assert.Equal(2, bids!.Count);

        // Newest first (BidTime descending) — tom's $18,000 bid was seeded after alice's
        // $15,000 one (DbInitializer.SeedDataAsync's fixed relative timestamps).
        Assert.Equal("tom", bids[0].Bidder);
        Assert.Equal(18000, bids[0].Amount);
        Assert.Equal("alice", bids[1].Bidder);
        Assert.Equal(15000, bids[1].Amount);
        Assert.All(bids, b => Assert.Equal("AcceptedBelowReserve", b.BidStatus));
    }

    // ── Critical 1 — concurrent bids on the same auction resolve to a coherent outcome ──
    //
    // A real end-to-end sanity/regression check of the atomic-accept wiring (real Mongo
    // transactions, real HTTP concurrency) — the deterministic proof of the exact race/downgrade
    // logic itself lives in BiddingService.UnitTests' BidPlacementUnitOfWorkTests, which mock
    // the unit-of-work seam directly (phase-end code review's own sanctioned alternative to a
    // black-box concurrency test, chosen specifically because the outcome of a genuine Mongo
    // commit-ordering race isn't independently controllable/observable from outside this
    // process, so a stronger assertion here would either be untestable or flaky). What IS
    // deterministic regardless of commit order, and asserted below: every one of N concurrently
    // submitted, strictly-ascending, distinct amounts is recorded exactly once; the single
    // highest amount ALWAYS ends up Accepted (nothing else submitted can ever be higher); and no
    // bid is ever lost or duplicated.

    [Fact]
    public async Task PlaceBid_WithManyConcurrentBidsOnTheSameAuction_RecordsACoherentOutcome()
    {
        var auctionId = Guid.NewGuid();
        var bus = factory.Services.GetRequiredService<MassTransit.IBus>();
        await bus.Publish(
            new AuctionCreated(
                auctionId, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                "dave", "", "Ford", "GT", 2020, "Red", 1,
                "http://images.local/ford-gt.jpg", null, "Live", ReservePrice: 0, null, null),
            TestContext.Current.CancellationToken);
        await RepositoryPolling.WaitForAuctionAsync(
            factory.Services, auctionId, TestContext.Current.CancellationToken,
            because: "AuctionCreatedConsumer should have stored a local projection before any bid is placed");

        var client = CreateClient(asUser: "eve", emailVerified: true); // dave is the seller, not eve
        var amounts = Enumerable.Range(1, 8).Select(i => i * 1000).ToArray(); // 1000, 2000, ..., 8000
        var maxAmount = amounts.Max();

        var responses = await Task.WhenAll(amounts.Select(amount =>
            client.PostAsJsonAsync(
                "api/bids", new PlaceBidDto { AuctionId = auctionId, Amount = amount },
                TestContext.Current.CancellationToken)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bidsResponse = await client.GetAsync($"api/bids/{auctionId}", TestContext.Current.CancellationToken);
        var bids = await bidsResponse.Content.ReadFromJsonAsync<List<BidDto>>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(bids);

        // Every submitted amount was recorded exactly once — no lost or duplicated writes.
        Assert.Equal(amounts.Length, bids!.Count);
        Assert.Equal(amounts.OrderDescending(), bids.Select(b => b.Amount).OrderDescending());

        // The single highest submitted amount can never legitimately lose the atomic claim (no
        // other submitted amount is ever higher, regardless of commit order) — it must be
        // Accepted (reserve price is 0), and it must be the ONLY bid whose amount matches it.
        var highestBid = Assert.Single(bids, b => b.Amount == maxAmount);
        Assert.Equal("Accepted", highestBid.BidStatus);
    }
}
