using System.Net;
using System.Net.Http.Json;
using AuctionService.Application.DTOs;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests for the Phase 11 Task 3 admin features, running the full HTTP + MVC + EF
/// Core pipeline: the "AdminOnly" policy's 401 (anonymous)/403 (non-admin) behavior across every
/// new admin endpoint, plus end-to-end coverage of auction moderation, banners, duration
/// settings, and the admin seller override on create.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class AdminFeaturesTests(CustomWebAppFactory factory)
{
    private HttpClient CreateClient(string? asUser, bool admin = false, bool emailVerified = true)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.EmailVerifiedHeader, emailVerified ? "true" : "false");
        }

        if (admin)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");

        return client;
    }

    private static CreateAuctionDto SampleCreateDto(string? seller = null, string? sellerEmail = null) => new()
    {
        Make = "Ford",
        Model = "GT",
        Color = "Red",
        Mileage = 1000,
        Year = 2020,
        ReservePrice = 20000,
        Images = [new ImageDto { Url = "https://example.com/car.jpg", SortOrder = 0 }],
        AuctionEnd = DateTime.UtcNow.AddDays(7),
        Seller = seller,
        SellerEmail = sellerEmail
    };

    private async Task<Guid> CreateAuctionAsAsync(string seller)
    {
        var client = CreateClient(asUser: seller);
        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var auction = await response.Content.ReadFromJsonAsync<AuctionDto>(TestContext.Current.CancellationToken);
        return auction!.Id;
    }

    // ── AdminOnly policy — 401/403 across every new admin endpoint ───────────

    [Theory]
    [InlineData("POST", "api/admin/auctions/00000000-0000-0000-0000-000000000000/end")]
    [InlineData("POST", "api/admin/auctions/00000000-0000-0000-0000-000000000000/cancel")]
    [InlineData("GET", "api/admin/auctions/stats")]
    [InlineData("GET", "api/admin/banners")]
    [InlineData("POST", "api/admin/banners")]
    [InlineData("PUT", "api/admin/banners/00000000-0000-0000-0000-000000000000")]
    [InlineData("DELETE", "api/admin/banners/00000000-0000-0000-0000-000000000000")]
    [InlineData("GET", "api/admin/settings/duration")]
    [InlineData("PUT", "api/admin/settings/duration")]
    public async Task AdminEndpoint_WhenAnonymous_Returns401(string method, string path)
    {
        var client = CreateClient(asUser: null);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "api/admin/auctions/00000000-0000-0000-0000-000000000000/end")]
    [InlineData("POST", "api/admin/auctions/00000000-0000-0000-0000-000000000000/cancel")]
    [InlineData("GET", "api/admin/auctions/stats")]
    [InlineData("GET", "api/admin/banners")]
    [InlineData("POST", "api/admin/banners")]
    [InlineData("PUT", "api/admin/banners/00000000-0000-0000-0000-000000000000")]
    [InlineData("DELETE", "api/admin/banners/00000000-0000-0000-0000-000000000000")]
    [InlineData("GET", "api/admin/settings/duration")]
    [InlineData("PUT", "api/admin/settings/duration")]
    public async Task AdminEndpoint_WhenAuthenticatedNonAdmin_Returns403(string method, string path)
    {
        var client = CreateClient(asUser: "regular-user", admin: false);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 3.2 — End auction ──────────────────────────────────────────────────────

    [Fact]
    public async Task EndAuction_AsAdmin_Returns200AndSetsAuctionEndToNow()
    {
        var id = await CreateAuctionAsAsync("end-seller");
        var admin = CreateClient(asUser: "admin-end", admin: true);

        var response = await admin.PostAsync(
            $"api/admin/auctions/{id}/end", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var anon = factory.CreateClient();
        var updated = await anon.GetFromJsonAsync<AuctionDetailDto>(
            $"api/auctions/{id}", TestContext.Current.CancellationToken);
        Assert.True(updated!.AuctionEnd <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task EndAuction_WhenNotFound_Returns404()
    {
        var admin = CreateClient(asUser: "admin-end-404", admin: true);

        var response = await admin.PostAsync(
            $"api/admin/auctions/{Guid.NewGuid()}/end", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── 3.3 — Cancel auction ────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAuction_AsAdmin_Returns200AndSetsStatusCancelled()
    {
        var id = await CreateAuctionAsAsync("cancel-seller");
        var admin = CreateClient(asUser: "admin-cancel", admin: true);

        var response = await admin.PostAsync(
            $"api/admin/auctions/{id}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var anon = factory.CreateClient();
        var updated = await anon.GetFromJsonAsync<AuctionDetailDto>(
            $"api/auctions/{id}", TestContext.Current.CancellationToken);
        Assert.Equal("Cancelled", updated!.Status);
    }

    // ── 3.7 — Stats ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_AsAdmin_ReturnsTotalAndByStatus()
    {
        await CreateAuctionAsAsync("stats-seller");
        var admin = CreateClient(asUser: "admin-stats", admin: true);

        var stats = await admin.GetFromJsonAsync<AuctionStatsDto>(
            "api/admin/auctions/stats", TestContext.Current.CancellationToken);

        Assert.NotNull(stats);
        Assert.True(stats!.Total >= 1);
        Assert.True(stats.ByStatus.ContainsKey("Live"));
    }

    // ── 3.1 — Admin seller override on create ───────────────────────────────────

    [Fact]
    public async Task CreateAuction_AsAdminWithExplicitSeller_HonorsSellerOverride()
    {
        var admin = CreateClient(asUser: "admin-creator", admin: true);

        var response = await admin.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(seller: "designated-seller"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auction = await response.Content.ReadFromJsonAsync<AuctionDto>(TestContext.Current.CancellationToken);
        Assert.Equal("designated-seller", auction!.Seller);
    }

    [Fact]
    public async Task CreateAuction_AsNonAdminWithExplicitSeller_IgnoresSellerOverride()
    {
        var client = CreateClient(asUser: "real-seller");

        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(seller: "someone-else"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auction = await response.Content.ReadFromJsonAsync<AuctionDto>(TestContext.Current.CancellationToken);
        Assert.Equal("real-seller", auction!.Seller);
    }

    // ── 3.4 — Duration validation ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAuction_WhenNonAdminAndAuctionEndBelowMinimum_Returns400ValidationProblem()
    {
        var client = CreateClient(asUser: "duration-test-seller");
        var dto = SampleCreateDto();
        var tooShort = new CreateAuctionDto
        {
            Make = dto.Make,
            Model = dto.Model,
            Color = dto.Color,
            Mileage = dto.Mileage,
            Year = dto.Year,
            ReservePrice = dto.ReservePrice,
            Images = dto.Images,
            AuctionEnd = DateTime.UtcNow.AddSeconds(1), // below the 1-hour default minimum
        };

        var response = await client.PostAsJsonAsync(
            "api/auctions", tooShort, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("AuctionEnd", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDurationLimits_IsAnonymousAndReturnsDefaults()
    {
        var client = factory.CreateClient();

        var limits = await client.GetFromJsonAsync<DurationLimitsDto>(
            "api/auctions/duration-limits", TestContext.Current.CancellationToken);

        Assert.NotNull(limits);
        Assert.True(limits!.MinDuration > TimeSpan.Zero);
        Assert.True(limits.MaxDuration > limits.MinDuration);
    }

    // ── 3.8 — Duration settings (DB override takes effect immediately) ──────────

    [Fact]
    public async Task UpdateDurationSettings_AsAdmin_TakesEffectImmediatelyOnDurationLimits()
    {
        var admin = CreateClient(asUser: "admin-settings", admin: true);
        var anon = factory.CreateClient();

        var putResponse = await admin.PutAsJsonAsync(
            "api/admin/settings/duration",
            new UpdateDurationSettingsDto { MinDuration = TimeSpan.FromMinutes(2), MaxDuration = TimeSpan.FromDays(45) },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var limits = await anon.GetFromJsonAsync<DurationLimitsDto>(
            "api/auctions/duration-limits", TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(2), limits!.MinDuration);
        Assert.Equal(TimeSpan.FromDays(45), limits.MaxDuration);
    }

    [Fact]
    public async Task UpdateDurationSettings_WhenInvalidRange_Returns400ValidationProblem()
    {
        var admin = CreateClient(asUser: "admin-settings-invalid", admin: true);

        var response = await admin.PutAsJsonAsync(
            "api/admin/settings/duration",
            new UpdateDurationSettingsDto { MinDuration = TimeSpan.FromDays(2), MaxDuration = TimeSpan.FromDays(1) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── 3.5 — Banners: admin CRUD + public read ─────────────────────────────────

    [Fact]
    public async Task CreateBanner_AsAdmin_Returns201AndIsVisibleOnPublicEndpoint()
    {
        var admin = CreateClient(asUser: "admin-banner-create", admin: true);
        var dto = new CreateBannerDto
        {
            Message = "Site-wide sale!",
            Scope = "Global",
            ActiveFrom = DateTime.UtcNow.AddMinutes(-1),
            ActiveUntil = DateTime.UtcNow.AddDays(1)
        };

        var response = await admin.PostAsJsonAsync(
            "api/admin/banners", dto, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var anon = factory.CreateClient();
        var active = await anon.GetFromJsonAsync<List<BannerDto>>(
            "api/banners?scope=Global", TestContext.Current.CancellationToken);

        Assert.Contains(active!, b => b.Message == "Site-wide sale!");
    }

    [Fact]
    public async Task CreateBanner_WithAuctionScopeAndNoAuctionId_Returns400()
    {
        var admin = CreateClient(asUser: "admin-banner-invalid", admin: true);
        var dto = new CreateBannerDto
        {
            Message = "x",
            Scope = "Auction",
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(1)
        };

        var response = await admin.PostAsJsonAsync(
            "api/admin/banners", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBanner_AsAdmin_RemovesItFromPublicEndpoint()
    {
        var admin = CreateClient(asUser: "admin-banner-delete", admin: true);
        var createResponse = await admin.PostAsJsonAsync(
            "api/admin/banners",
            new CreateBannerDto
            {
                Message = "Temporary",
                Scope = "HomePage",
                ActiveFrom = DateTime.UtcNow.AddMinutes(-1),
                ActiveUntil = DateTime.UtcNow.AddDays(1)
            },
            TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<BannerDto>(TestContext.Current.CancellationToken);

        var deleteResponse = await admin.DeleteAsync(
            $"api/admin/banners/{created!.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var anon = factory.CreateClient();
        var active = await anon.GetFromJsonAsync<List<BannerDto>>(
            "api/banners?scope=HomePage", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(active!, b => b.Id == created.Id);
    }
}
