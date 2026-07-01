using System.Security.Claims;
using AuctionService.API.Controllers;
using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for presigned-upload issuance (Phase 1 Task 18.1/18.3): the
/// <see cref="AuctionImageAppService"/> validation rules and the
/// <see cref="AuctionsController.CreateUploadUrl"/> HTTP mapping.
/// </summary>
public class UploadUrlTests
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
    public async Task CreateUploadUrlAsync_WhenContentTypeNotAllowed_ReturnsInvalidContentType()
    {
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateUploadUrlAsync(
            new UploadUrlRequest { ContentType = "image/gif", SizeBytes = 1_000 });

        Assert.Equal(UploadUrlOutcome.InvalidContentType, outcome);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateUploadUrlAsync_WhenSizeExceedsLimit_ReturnsInvalidSize()
    {
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateUploadUrlAsync(
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 6 * 1024 * 1024 });

        Assert.Equal(UploadUrlOutcome.InvalidSize, outcome);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateUploadUrlAsync_WhenSizeNonPositive_ReturnsInvalidSize()
    {
        var storage = Substitute.For<IImageStorage>();
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateUploadUrlAsync(
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 0 });

        Assert.Equal(UploadUrlOutcome.InvalidSize, outcome);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateUploadUrlAsync_WhenValid_ReturnsSuccessWithResponse()
    {
        var storage = Substitute.For<IImageStorage>();
        var presigned = new PresignedUpload(
            Guid.NewGuid().ToString(), "http://upload", "http://obj", DateTime.UtcNow.AddMinutes(5));
        storage.CreatePresignedUpload(Arg.Any<string>(), Arg.Any<long>()).Returns(presigned);
        var sut = new AuctionImageAppService(storage, Options.Create(SampleImagesOptions()));

        var (outcome, response) = await sut.CreateUploadUrlAsync(
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 1_000 });

        Assert.Equal(UploadUrlOutcome.Success, outcome);
        Assert.NotNull(response);
    }

    // ── Controller-level (AuctionsController.CreateUploadUrl) ────────────────

    private readonly IAuctionService _service = Substitute.For<IAuctionService>();
    private readonly IAuctionImageService _imageService = Substitute.For<IAuctionImageService>();

    // Mirrors the BuildController/ClaimsPrincipal pattern established in
    // AuctionsControllerTests (Task 14) — duplicated here since each xUnit test class
    // is independently instantiated. email_verified defaults to true so the
    // CreateUploadUrl action body (not just the verification gate) is exercised.
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

    // Covers "unauthenticated returns 401": [Authorize] is what produces the 401 via
    // the ASP.NET Core authentication/authorization middleware pipeline, which does not
    // run when the action is invoked directly in a unit test — so we assert the
    // attribute itself is present rather than simulating the pipeline.
    [Fact]
    public void CreateUploadUrl_HasAuthorizeAttribute()
    {
        var method = typeof(AuctionsController).GetMethod(nameof(AuctionsController.CreateUploadUrl));

        Assert.NotNull(method);
        Assert.True(method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Any());
    }

    [Fact]
    public async Task CreateUploadUrl_WhenInvalidContentType_Returns400BadRequest()
    {
        _imageService.CreateUploadUrlAsync(Arg.Any<UploadUrlRequest>())
            .Returns((UploadUrlOutcome.InvalidContentType, (UploadUrlResponse?)null));
        var controller = BuildController();

        var result = await controller.CreateUploadUrl(
            new UploadUrlRequest { ContentType = "image/gif", SizeBytes = 1_000 });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateUploadUrl_WhenInvalidSize_Returns400BadRequest()
    {
        _imageService.CreateUploadUrlAsync(Arg.Any<UploadUrlRequest>())
            .Returns((UploadUrlOutcome.InvalidSize, (UploadUrlResponse?)null));
        var controller = BuildController();

        var result = await controller.CreateUploadUrl(
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 999_999_999 });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateUploadUrl_WhenSuccess_ReturnsOkWithResponse()
    {
        var response = new UploadUrlResponse
        {
            Key = Guid.NewGuid().ToString(),
            UploadUrl = "http://upload",
            ObjectUrl = "http://obj",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        _imageService.CreateUploadUrlAsync(Arg.Any<UploadUrlRequest>())
            .Returns((UploadUrlOutcome.Success, response));
        var controller = BuildController();

        var result = await controller.CreateUploadUrl(
            new UploadUrlRequest { ContentType = "image/jpeg", SizeBytes = 1_000 });

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
