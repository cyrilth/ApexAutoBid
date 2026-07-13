using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Mapster;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for post-sale contact exchange (Requirements §3.1 / Tasks.md Phase 5 Task 19):
/// <c>AuctionAppService.GetAuctionByIdAsync</c> conditionally reveals <c>SellerEmail</c>/
/// <c>WinnerEmail</c> on a SOLD auction (<c>Status = Finished</c> with a recorded
/// <c>Winner</c>), and only to the exact counterparty. Covers all five states required by
/// Task 19.2: seller sees WinnerEmail, winner sees SellerEmail, an unrelated authenticated
/// user sees neither, an anonymous caller sees neither, and an unsold auction exposes neither
/// to anyone (including the seller and the would-be winner of the high bid).
/// <para>
/// The unit seam is <see cref="AuctionAppService"/> itself: <see cref="IAuctionRepository"/> is
/// substituted (returns a fixed <see cref="Auction"/> per test) while the real Mapster
/// <c>AuctionMappingConfig</c> is scanned exactly like <c>ApplicationServiceExtensions</c> does
/// — mirroring the pattern already established in SearchService.UnitTests/SearchAppServiceTests.cs
/// — so the Auction → AuctionDto → AuctionDetailDto mapping is genuinely exercised, not mocked
/// away.
/// </para>
/// </summary>
public class PostSaleContactExchangeTests
{
    private const string Seller = "bob";
    private const string SellerEmail = "bob@apexautobid.local";
    private const string Winner = "alice";
    private const string WinnerEmail = "alice@apexautobid.local";

    private static IMapper BuildMapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(AuctionAppService).Assembly);
        return new Mapper(config);
    }

    private static ImagesOptions SampleImagesOptions() => new()
    {
        PublicBaseUrl = "http://localhost:9000",
        Bucket = "auction-images",
        MaxSizeMB = 5,
        MaxPerAuction = 10
    };

    private static IPlatformSettingsService BuildDurationSettings()
    {
        var settings = Substitute.For<IPlatformSettingsService>();
        settings.GetEffectiveDurationBoundsAsync()
            .Returns((TimeSpan.FromMinutes(1), TimeSpan.FromDays(365)));
        return settings;
    }

    private static AuctionAppService BuildSut(IAuctionRepository repository) =>
        new(
            repository,
            BuildMapper(),
            Substitute.For<IPublishEndpoint>(),
            Substitute.For<IImageStorage>(),
            Options.Create(SampleImagesOptions()),
            BuildDurationSettings());

    private static Auction SoldAuction() => new()
    {
        Id = Guid.NewGuid(),
        Seller = Seller,
        SellerEmail = SellerEmail,
        Winner = Winner,
        WinnerEmail = WinnerEmail,
        SoldAmount = 25000,
        ReservePrice = 20000,
        AuctionEnd = DateTime.UtcNow.AddDays(-2),
        Status = Status.Finished,
        Item = new Item
        {
            Make = "Ford",
            Model = "Model T",
            Color = "Rust",
            Year = 1938,
            Mileage = 150000,
            Images = [new ItemImage { Url = "http://localhost:9000/auction-images/ford-model-t.jpg", SortOrder = 0 }]
        }
    };

    private static Auction LiveAuction() => new()
    {
        Id = Guid.NewGuid(),
        Seller = Seller,
        SellerEmail = SellerEmail,
        CurrentHighBid = 18000,
        ReservePrice = 20000,
        AuctionEnd = DateTime.UtcNow.AddDays(10),
        Status = Status.Live,
        Item = new Item
        {
            Make = "Ford",
            Model = "GT",
            Color = "White",
            Year = 2020,
            Mileage = 50000,
            Images = [new ItemImage { Url = "http://localhost:9000/auction-images/ford-gt.jpg", SortOrder = 0 }]
        }
    };

    private static IAuctionRepository RepositoryReturning(Auction auction)
    {
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        return repository;
    }

    // ── 19.2.1 — seller sees WinnerEmail, not SellerEmail ────────────────────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenCallerIsSellerOfSoldAuction_ReturnsWinnerEmailOnly()
    {
        var auction = SoldAuction();
        var sut = BuildSut(RepositoryReturning(auction));

        var dto = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: Seller);

        Assert.NotNull(dto);
        Assert.Equal(WinnerEmail, dto!.WinnerEmail);
        Assert.Null(dto.SellerEmail);
    }

    // ── 19.2.2 — winner sees SellerEmail, not WinnerEmail ────────────────────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenCallerIsWinnerOfSoldAuction_ReturnsSellerEmailOnly()
    {
        var auction = SoldAuction();
        var sut = BuildSut(RepositoryReturning(auction));

        var dto = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: Winner);

        Assert.NotNull(dto);
        Assert.Equal(SellerEmail, dto!.SellerEmail);
        Assert.Null(dto.WinnerEmail);
    }

    // ── 19.2.3 — unrelated authenticated user sees neither ───────────────────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenCallerIsUnrelatedAuthenticatedUser_ReturnsNeitherEmail()
    {
        var auction = SoldAuction();
        var sut = BuildSut(RepositoryReturning(auction));

        var dto = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: "tom");

        Assert.NotNull(dto);
        Assert.Null(dto!.SellerEmail);
        Assert.Null(dto.WinnerEmail);
    }

    // ── 19.2.4 — anonymous caller sees neither ───────────────────────────────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenCallerIsAnonymous_ReturnsNeitherEmail()
    {
        var auction = SoldAuction();
        var sut = BuildSut(RepositoryReturning(auction));

        var dto = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: null);

        Assert.NotNull(dto);
        Assert.Null(dto!.SellerEmail);
        Assert.Null(dto.WinnerEmail);
    }

    // ── 19.2.5 — unsold (Live) auction exposes neither to anyone ─────────────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenAuctionNotSold_ReturnsNeitherEmailEvenToSellerOrHighBidder()
    {
        var auction = LiveAuction();
        var sut = BuildSut(RepositoryReturning(auction));

        var sellerView = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: Seller);
        // "alice" isn't recorded as Winner (the auction hasn't finished) even though she might
        // be the current high bidder in a fuller scenario — there is no Winner yet, so this
        // caller can never satisfy the "requestingUser == auction.Winner" condition regardless.
        var otherView = await sut.GetAuctionByIdAsync(auction.Id, requestingUser: Winner);

        Assert.NotNull(sellerView);
        Assert.Null(sellerView!.SellerEmail);
        Assert.Null(sellerView.WinnerEmail);

        Assert.NotNull(otherView);
        Assert.Null(otherView!.SellerEmail);
        Assert.Null(otherView.WinnerEmail);
    }

    // ── Not-found passthrough — unaffected by the contact-exchange logic ────────
    [Fact]
    public async Task GetAuctionByIdAsync_WhenAuctionNotFound_ReturnsNull()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var missingId = Guid.NewGuid();
        repository.GetByIdAsync(missingId).Returns((Auction?)null);
        var sut = BuildSut(repository);

        var dto = await sut.GetAuctionByIdAsync(missingId, requestingUser: Seller);

        Assert.Null(dto);
    }
}
