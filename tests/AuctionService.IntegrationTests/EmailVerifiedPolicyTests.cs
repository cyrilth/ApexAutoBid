using System.Net;
using System.Net.Http.Json;
using AuctionService.Application.DTOs;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Integration tests for the "EmailVerified" authorization policy (Phase 3 Task 19), covering
/// the full 3-way behavior matrix across all three mutating auction endpoints: a verified user
/// succeeds, an authenticated-but-unverified user gets 403 — this is the NEW coverage; before
/// this task only CreateAuction enforced this at all, and even then via an ad-hoc in-body check
/// that no integration test ever exercised live through the real authorization pipeline — and an
/// anonymous caller still gets 401, exactly as before. Each PUT/DELETE test creates its own
/// throwaway auction first (via a verified POST) rather than depending on seeded data or another
/// test's auction, so execution order/parallelism within the shared
/// <see cref="CustomWebAppFactory"/>-backed collection can't affect these tests or be affected
/// by them.
/// </summary>
[Collection(AuctionServiceApiCollection.Name)]
public class EmailVerifiedPolicyTests(CustomWebAppFactory factory)
{
    private static CreateAuctionDto SampleCreateDto() => new()
    {
        Make = "Ford",
        Model = "GT",
        Color = "Red",
        Mileage = 1000,
        Year = 2020,
        ReservePrice = 20000,
        // External URL — the integration host has no MinIO, so a platform-hosted URL would fail
        // the HEAD size check; external URLs are exempt (matches AuditTrailTests.cs's approach).
        Images = [new ImageDto { Url = "https://example.com/car.jpg", SortOrder = 0 }],
        AuctionEnd = DateTime.UtcNow.AddDays(7),
    };

    private HttpClient CreateClient(string? asUser, bool? emailVerified = null)
    {
        var client = factory.CreateClient();
        if (asUser is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, asUser);
        }

        if (emailVerified is not null)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.EmailVerifiedHeader, emailVerified.Value ? "true" : "false");
        }

        return client;
    }

    private async Task<Guid> CreateOwnedAuctionAsync(string owner)
    {
        var client = CreateClient(asUser: owner, emailVerified: true);
        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var auction = await response.Content.ReadFromJsonAsync<AuctionDto>(TestContext.Current.CancellationToken);
        return auction!.Id;
    }

    // ── POST api/auctions ──────────────────────────────────────────────────────
    [Fact]
    public async Task CreateAuction_WhenVerified_Returns201()
    {
        var client = CreateClient(asUser: "policy-test-verified-create", emailVerified: true);

        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateAuction_WhenUnverified_Returns403()
    {
        var client = CreateClient(asUser: "policy-test-unverified-create", emailVerified: false);

        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAuction_WhenAnonymous_Returns401()
    {
        var client = CreateClient(asUser: null);

        var response = await client.PostAsJsonAsync(
            "api/auctions", SampleCreateDto(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── PUT api/auctions/{id} ──────────────────────────────────────────────────
    [Fact]
    public async Task UpdateAuction_WhenVerified_Returns200()
    {
        const string owner = "policy-test-verified-update";
        var id = await CreateOwnedAuctionAsync(owner);
        var client = CreateClient(asUser: owner, emailVerified: true);

        var response = await client.PutAsJsonAsync(
            $"api/auctions/{id}", new UpdateAuctionDto { Color = "Blue" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAuction_WhenUnverified_Returns403()
    {
        // A random, guaranteed-nonexistent id is sufficient and deliberate: the policy runs in
        // the authorization middleware BEFORE the controller action (and its own "does this
        // auction exist"/ownership logic) ever executes, so a 403 here proves the POLICY
        // rejected the request — not the not-found or ownership path.
        var client = CreateClient(asUser: "policy-test-unverified-update", emailVerified: false);

        var response = await client.PutAsJsonAsync(
            $"api/auctions/{Guid.NewGuid()}", new UpdateAuctionDto { Color = "Blue" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAuction_WhenAnonymous_Returns401()
    {
        var client = CreateClient(asUser: null);

        var response = await client.PutAsJsonAsync(
            $"api/auctions/{Guid.NewGuid()}", new UpdateAuctionDto { Color = "Blue" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── DELETE api/auctions/{id} ────────────────────────────────────────────────
    [Fact]
    public async Task DeleteAuction_WhenVerified_Returns200()
    {
        const string owner = "policy-test-verified-delete";
        var id = await CreateOwnedAuctionAsync(owner);
        var client = CreateClient(asUser: owner, emailVerified: true);

        var response = await client.DeleteAsync($"api/auctions/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAuction_WhenUnverified_Returns403()
    {
        // Same random-id reasoning as UpdateAuction_WhenUnverified_Returns403 above.
        var client = CreateClient(asUser: "policy-test-unverified-delete", emailVerified: false);

        var response = await client.DeleteAsync($"api/auctions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAuction_WhenAnonymous_Returns401()
    {
        var client = CreateClient(asUser: null);

        var response = await client.DeleteAsync($"api/auctions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST api/auctions/upload-url / api/auctions/thumbnail (Task 19 follow-up) ──
    //
    // Unverified→403 only, deliberately — no verified-success test for either endpoint: that
    // would need real MinIO storage this integration host doesn't have wired up, whereas a
    // 403-before-action test needs no storage at all (decompile-confirmed: the policy runs in
    // the authorization middleware before the controller action — and its imageService call —
    // ever executes). Anonymous→401 for these two isn't added either: it exercises the exact
    // same authentication short-circuit as the CRUD triad's anonymous tests above, just on a
    // different path — not new coverage worth the extra two tests.
    [Fact]
    public async Task CreateUploadUrl_WhenUnverified_Returns403()
    {
        var client = CreateClient(asUser: "policy-test-unverified-upload-url", emailVerified: false);

        var response = await client.PostAsJsonAsync(
            "api/auctions/upload-url",
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 1_000 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateThumbnail_WhenUnverified_Returns403()
    {
        var client = CreateClient(asUser: "policy-test-unverified-thumbnail", emailVerified: false);

        var response = await client.PostAsJsonAsync(
            "api/auctions/thumbnail",
            new ThumbnailRequest { Key = Guid.NewGuid().ToString() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
