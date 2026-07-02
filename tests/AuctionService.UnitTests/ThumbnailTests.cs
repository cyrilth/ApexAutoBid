using System.Security.Claims;
using AuctionService.API.Controllers;
using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for thumbnail generation (Phase 1 Task 18.4/18.5): the
/// <see cref="AuctionImageAppService"/> SSRF guard and the
/// <see cref="AuctionsController.CreateThumbnail"/> HTTP mapping.
/// </summary>
public class ThumbnailTests
{
    private static ImagesOptions SampleImagesOptions() => new()
    {
        PublicBaseUrl = "http://localhost:9000",
        Bucket = "auction-images",
        MaxSizeMB = 5,
        MaxPerAuction = 10
    };

    // ── Service-level (AuctionImageAppService) ───────────────────────────────

    [Fact]
    public async Task CreateThumbnailAsync_WhenKeyIsValidGuid_ReturnsSuccessWithUrl()
    {
        var key = Guid.NewGuid().ToString();
        var expectedUrl = $"http://localhost:9000/auction-images/thumbs/{key}.webp";
        var storage = Substitute.For<IImageStorage>();
        storage.CreateThumbnailAsync(key, Arg.Any<CancellationToken>()).Returns(expectedUrl);
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateThumbnailAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(ThumbnailOutcome.Success, outcome);
        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response!.ThumbnailUrl);
    }

    [Theory]
    [InlineData("http://evil.com/x")]
    [InlineData("../secret")]
    [InlineData("not-a-guid")]
    public async Task CreateThumbnailAsync_WhenKeyIsNotAGuid_ReturnsInvalidKeyWithoutCallingStorage(string key)
    {
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateThumbnailAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(ThumbnailOutcome.InvalidKey, outcome);
        Assert.Null(response);
        await storage.DidNotReceive().CreateThumbnailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Controller-level (AuctionsController.CreateThumbnail) ────────────────

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
    public async Task CreateThumbnail_WhenInvalidKey_Returns400BadRequest()
    {
        _imageService.CreateThumbnailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ThumbnailOutcome.InvalidKey, (ThumbnailResponse?)null));
        var controller = BuildController();

        var result = await controller.CreateThumbnail(new ThumbnailRequest { Key = "http://evil.com/x" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateThumbnail_WhenSourceNotFound_Returns404NotFound()
    {
        _imageService.CreateThumbnailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ThumbnailOutcome.SourceNotFound, (ThumbnailResponse?)null));
        var controller = BuildController();

        var result = await controller.CreateThumbnail(new ThumbnailRequest { Key = Guid.NewGuid().ToString() });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateThumbnail_WhenSuccess_ReturnsOkWithResponse()
    {
        var response = new ThumbnailResponse { ThumbnailUrl = "http://localhost:9000/auction-images/thumbs/x.webp" };
        _imageService.CreateThumbnailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ThumbnailOutcome.Success, response));
        var controller = BuildController();

        var result = await controller.CreateThumbnail(new ThumbnailRequest { Key = Guid.NewGuid().ToString() });

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
