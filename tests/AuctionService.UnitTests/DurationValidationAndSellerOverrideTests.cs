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
/// Unit tests for Phase 11 Task 3.1 (admin seller override) and Task 3.4 (auction-duration
/// validation, including the admin exemption and the admin ownership bypass on update) in
/// <see cref="AuctionAppService"/>.
/// </summary>
public class DurationValidationAndSellerOverrideTests
{
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

    private static IPlatformSettingsService DurationSettings(TimeSpan min, TimeSpan max)
    {
        var settings = Substitute.For<IPlatformSettingsService>();
        settings.GetEffectiveDurationBoundsAsync().Returns((min, max));
        return settings;
    }

    private static AuctionAppService BuildSut(
        IAuctionRepository repository, IPlatformSettingsService durationSettings, IMapper? mapper = null)
    {
        // Defaults every test's repository to a "successful save" so tests focused on
        // duration/seller-override logic don't each need to separately stub this — NSubstitute
        // otherwise defaults Task<bool> to false, which would masquerade as SaveFailed.
        repository.SaveChangesAsync().Returns(true);

        return new AuctionAppService(
            repository,
            mapper ?? BuildMapper(),
            Substitute.For<IPublishEndpoint>(),
            Substitute.For<IImageStorage>(),
            Options.Create(SampleImagesOptions()),
            durationSettings);
    }

    private static CreateAuctionDto SampleCreateDto(DateTime auctionEnd, string? seller = null, string? sellerEmail = null) => new()
    {
        Make = "Ford",
        Model = "GT",
        Color = "Red",
        Mileage = 1000,
        Year = 2020,
        ReservePrice = 20000,
        Images = [new ImageDto { Url = "http://ext/img.jpg", SortOrder = 0 }],
        AuctionEnd = auctionEnd,
        Seller = seller,
        SellerEmail = sellerEmail
    };

    private static Auction SampleAuction(string seller = "bob") => new()
    {
        Id = Guid.NewGuid(),
        Seller = seller,
        SellerEmail = $"{seller}@apexautobid.local",
        AuctionEnd = DateTime.UtcNow.AddDays(7),
        Status = Status.Live,
        Item = new Item
        {
            Make = "Ford",
            Model = "GT",
            Color = "Red",
            Year = 2020,
            Mileage = 1000,
            Images = [new ItemImage { Url = "http://ext/img.jpg", SortOrder = 0 }]
        }
    };

    // ── 3.4 — Create: duration bounds ────────────────────────────────────────

    [Fact]
    public async Task CreateAuctionAsync_WhenNonAdminAndAuctionEndBelowMin_ReturnsInvalidDuration()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        // 30 minutes from now — below the 1-hour minimum.
        var dto = SampleCreateDto(DateTime.UtcNow.AddMinutes(30));

        var result = await sut.CreateAuctionAsync(dto, "bob", "bob@x", isAdmin: false);

        Assert.Equal(AuctionWriteResult.InvalidDuration, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenNonAdminAndAuctionEndAboveMax_ReturnsInvalidDuration()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(200));

        var result = await sut.CreateAuctionAsync(dto, "bob", "bob@x", isAdmin: false);

        Assert.Equal(AuctionWriteResult.InvalidDuration, result.Status);
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenNonAdminAndAuctionEndWithinBounds_Succeeds()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(7));

        var result = await sut.CreateAuctionAsync(dto, "bob", "bob@x", isAdmin: false);

        Assert.Equal(AuctionWriteResult.Success, result.Status);
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenAdminAndAuctionEndOutsideBounds_IsExemptAndSucceeds()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        // Only 5 minutes — would fail the 1-hour minimum for a non-admin.
        var dto = SampleCreateDto(DateTime.UtcNow.AddMinutes(5));

        var result = await sut.CreateAuctionAsync(dto, "admin1", "admin1@x", isAdmin: true);

        Assert.Equal(AuctionWriteResult.Success, result.Status);
    }

    // ── 3.1 — Create: seller override ────────────────────────────────────────

    [Fact]
    public async Task CreateAuctionAsync_WhenNonAdminSuppliesExplicitSeller_IsIgnored()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);
        Auction? captured = null;
        repository.When(r => r.Add(Arg.Any<Auction>())).Do(ci => captured = ci.Arg<Auction>());

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(7), seller: "someone-else", sellerEmail: "hacker@x");

        var result = await sut.CreateAuctionAsync(dto, "bob", "bob@apexautobid.local", isAdmin: false);

        Assert.Equal(AuctionWriteResult.Success, result.Status);
        Assert.NotNull(captured);
        Assert.Equal("bob", captured!.Seller);
        Assert.Equal("bob@apexautobid.local", captured.SellerEmail);
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenAdminSuppliesExplicitSellerAndEmail_IsHonored()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);
        Auction? captured = null;
        repository.When(r => r.Add(Arg.Any<Auction>())).Do(ci => captured = ci.Arg<Auction>());

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(7), seller: "alice", sellerEmail: "alice@apexautobid.local");

        var result = await sut.CreateAuctionAsync(dto, "admin1", "admin1@apexautobid.local", isAdmin: true);

        Assert.Equal(AuctionWriteResult.Success, result.Status);
        Assert.NotNull(captured);
        Assert.Equal("alice", captured!.Seller);
        Assert.Equal("alice@apexautobid.local", captured.SellerEmail);
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenAdminSuppliesSellerWithoutEmail_SellerEmailIsEmpty()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);
        Auction? captured = null;
        repository.When(r => r.Add(Arg.Any<Auction>())).Do(ci => captured = ci.Arg<Auction>());

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(7), seller: "alice", sellerEmail: null);

        await sut.CreateAuctionAsync(dto, "admin1", "admin1@apexautobid.local", isAdmin: true);

        Assert.NotNull(captured);
        Assert.Equal("alice", captured!.Seller);
        Assert.Equal(string.Empty, captured.SellerEmail);
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenAdminSuppliesNoExplicitSeller_FallsBackToCaller()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);
        Auction? captured = null;
        repository.When(r => r.Add(Arg.Any<Auction>())).Do(ci => captured = ci.Arg<Auction>());

        var dto = SampleCreateDto(DateTime.UtcNow.AddDays(7));

        await sut.CreateAuctionAsync(dto, "admin1", "admin1@apexautobid.local", isAdmin: true);

        Assert.NotNull(captured);
        Assert.Equal("admin1", captured!.Seller);
    }

    // ── 3.4 — Update: duration bounds ────────────────────────────────────────

    [Fact]
    public async Task UpdateAuctionAsync_WhenNonAdminAndNewAuctionEndOutsideBounds_ReturnsInvalidDuration()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var result = await sut.UpdateAuctionAsync(
            auction.Id,
            new UpdateAuctionDto { AuctionEnd = DateTime.UtcNow.AddMinutes(1) },
            "bob",
            isAdmin: false);

        Assert.Equal(AuctionWriteResult.InvalidDuration, result);
    }

    [Fact]
    public async Task UpdateAuctionAsync_WhenAdminShortensLiveAuctionOutsideBounds_IsExemptAndSucceeds()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var newEnd = DateTime.UtcNow.AddMinutes(1);
        var result = await sut.UpdateAuctionAsync(
            auction.Id, new UpdateAuctionDto { AuctionEnd = newEnd }, "admin1", isAdmin: true);

        Assert.Equal(AuctionWriteResult.Success, result);
        Assert.Equal(newEnd, auction.AuctionEnd);
    }

    [Fact]
    public async Task UpdateAuctionAsync_WhenNoAuctionEndSupplied_SkipsDurationValidation()
    {
        var auction = SampleAuction();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var result = await sut.UpdateAuctionAsync(
            auction.Id, new UpdateAuctionDto { Color = "Blue" }, "bob", isAdmin: false);

        Assert.Equal(AuctionWriteResult.Success, result);
        await durationSettings.DidNotReceive().GetEffectiveDurationBoundsAsync();
    }

    // ── Update: admin ownership bypass ────────────────────────────────────────

    [Fact]
    public async Task UpdateAuctionAsync_WhenNonAdminAndNotOwner_ReturnsForbidden()
    {
        var auction = SampleAuction(seller: "bob");
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var result = await sut.UpdateAuctionAsync(
            auction.Id, new UpdateAuctionDto { Color = "Blue" }, "alice", isAdmin: false);

        Assert.Equal(AuctionWriteResult.Forbidden, result);
    }

    [Fact]
    public async Task UpdateAuctionAsync_WhenAdminAndNotOwner_BypassesOwnershipCheck()
    {
        var auction = SampleAuction(seller: "bob");
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(auction.Id).Returns(auction);
        var durationSettings = DurationSettings(TimeSpan.FromHours(1), TimeSpan.FromDays(90));
        var sut = BuildSut(repository, durationSettings);

        var result = await sut.UpdateAuctionAsync(
            auction.Id, new UpdateAuctionDto { Color = "Blue" }, "admin1", isAdmin: true);

        Assert.Equal(AuctionWriteResult.Success, result);
        Assert.Equal("Blue", auction.Item.Color);
    }

    // ── 3.8 — GetDurationLimitsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetDurationLimitsAsync_ReturnsBoundsFromDurationSettingsService()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var durationSettings = DurationSettings(TimeSpan.FromMinutes(1), TimeSpan.FromDays(30));
        var sut = BuildSut(repository, durationSettings);

        var limits = await sut.GetDurationLimitsAsync();

        Assert.Equal(TimeSpan.FromMinutes(1), limits.MinDuration);
        Assert.Equal(TimeSpan.FromDays(30), limits.MaxDuration);
    }
}
