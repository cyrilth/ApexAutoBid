using System.Security.Claims;
using AuctionService.API.Controllers;
using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Contracts;
using MapsterMapper;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for server-side gallery enforcement (Phase 1 Task 18.6): the image-count
/// bound in <see cref="AuctionAppService.CreateAuctionAsync"/> and the corresponding
/// <see cref="AuctionsController.CreateAuction"/> HTTP mapping.
/// </summary>
public class GalleryEnforcementTests
{
    private static ImagesOptions SampleImagesOptions() => new()
    {
        PublicBaseUrl = "http://localhost:9000",
        Bucket = "auction-images",
        MaxSizeMB = 5,
        MaxPerAuction = 10
    };

    private static CreateAuctionDto SampleDto(List<ImageDto> images) => new()
    {
        Make = "Ford",
        Model = "GT",
        Color = "Red",
        Mileage = 1000,
        Year = 2020,
        ReservePrice = 20000,
        Images = images,
        AuctionEnd = DateTime.UtcNow.AddDays(7),
    };

    private static List<ImageDto> ExternalImages(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new ImageDto { Url = "http://ext/img.jpg", SortOrder = 0 })
            .ToList();

    // ── Service-level (AuctionAppService) ────────────────────────────────────

    [Fact]
    public async Task CreateAuctionAsync_WhenZeroImages_ReturnsInvalidImagesWithoutTouchingRepository()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var mapper = Substitute.For<IMapper>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionAppService(
            repository, mapper, publishEndpoint, storage, Options.Create(SampleImagesOptions()));

        var result = await sut.CreateAuctionAsync(SampleDto([]), "bob", "bob@x");

        Assert.Equal(AuctionWriteResult.InvalidImages, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
        mapper.DidNotReceive().Map<AuctionDto>(Arg.Any<Auction>());
        await publishEndpoint.DidNotReceive().Publish(Arg.Any<AuctionCreated>());
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenOverLimitImageCount_ReturnsInvalidImagesWithoutTouchingRepository()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var mapper = Substitute.For<IMapper>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionAppService(
            repository, mapper, publishEndpoint, storage, Options.Create(SampleImagesOptions()));

        var result = await sut.CreateAuctionAsync(SampleDto(ExternalImages(11)), "bob", "bob@x");

        Assert.Equal(AuctionWriteResult.InvalidImages, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
    }

    // ── Platform-hosted gallery hardening (verified code review fixes) ──────

    [Fact]
    public async Task CreateAuctionAsync_WhenPlatformUrlIsPathTraversal_ReturnsInvalidImagesWithoutCallingStorage()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var mapper = Substitute.For<IMapper>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionAppService(
            repository, mapper, publishEndpoint, storage, Options.Create(SampleImagesOptions()));

        var images = new List<ImageDto>
        {
            new() { Url = "http://localhost:9000/auction-images/../../evil", SortOrder = 0 }
        };

        var result = await sut.CreateAuctionAsync(SampleDto(images), "bob", "bob@x");

        Assert.Equal(AuctionWriteResult.InvalidImages, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
        await storage.DidNotReceive().TryGetObjectSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenPlatformImageMissing_ReturnsInvalidImages()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var mapper = Substitute.For<IMapper>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var storage = Substitute.For<IImageStorage>();
        storage.TryGetObjectSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        var sut = new AuctionAppService(
            repository, mapper, publishEndpoint, storage, Options.Create(SampleImagesOptions()));

        var key = Guid.NewGuid().ToString();
        var images = new List<ImageDto>
        {
            new() { Url = $"http://localhost:9000/auction-images/{key}", SortOrder = 0 }
        };

        var result = await sut.CreateAuctionAsync(SampleDto(images), "bob", "bob@x");

        Assert.Equal(AuctionWriteResult.InvalidImages, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAuctionAsync_WhenPlatformImageOversized_ReturnsInvalidImagesWithoutDeleting()
    {
        var repository = Substitute.For<IAuctionRepository>();
        var mapper = Substitute.For<IMapper>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var storage = Substitute.For<IImageStorage>();
        storage.TryGetObjectSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(6L * 1024 * 1024);
        var sut = new AuctionAppService(
            repository, mapper, publishEndpoint, storage, Options.Create(SampleImagesOptions()));

        var key = Guid.NewGuid().ToString();
        var images = new List<ImageDto>
        {
            new() { Url = $"http://localhost:9000/auction-images/{key}", SortOrder = 0 }
        };

        var result = await sut.CreateAuctionAsync(SampleDto(images), "bob", "bob@x");

        Assert.Equal(AuctionWriteResult.InvalidImages, result.Status);
        Assert.Null(result.Auction);
        repository.DidNotReceive().Add(Arg.Any<Auction>());
        await repository.DidNotReceive().SaveChangesAsync();
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Controller-level (AuctionsController.CreateAuction) ──────────────────

    private readonly IAuctionService _service = Substitute.For<IAuctionService>();
    private readonly IAuctionImageService _imageService = Substitute.For<IAuctionImageService>();

    // Mirrors the BuildController/ClaimsPrincipal pattern established in
    // AuctionsControllerTests (Task 14).
    private AuctionsController BuildController(bool emailVerified = true, string username = "seller-bob")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Email, "bob@apexautobid.local"),
            new("email_verified", emailVerified ? "true" : "false"),
        };

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

        return new AuctionsController(_service, _imageService, NullLogger<AuctionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };
    }

    [Fact]
    public async Task CreateAuction_WhenGalleryInvalid_Returns400BadRequest()
    {
        _service.CreateAuctionAsync(Arg.Any<CreateAuctionDto>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new AuctionCreateResult(AuctionWriteResult.InvalidImages, null));
        var controller = BuildController();

        var result = await controller.CreateAuction(SampleDto(ExternalImages(11)));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
