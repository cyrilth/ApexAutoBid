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
    // AuctionsControllerTests (Task 14). email_verified is always "true": Phase 3 Task 19's
    // follow-up round converted this endpoint's own in-body check to the "EmailVerified" policy,
    // so nothing in THIS controller method reads the claim anymore — no test in this file needs
    // a "false" variant, matching AuctionsControllerTests.cs's identical Task 19 cleanup.
    private AuctionsController BuildController(string username = "seller-bob")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Email, "bob@apexautobid.local"),
            new("email_verified", "true"),
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

    // ── Phase 3 Task 19 follow-up — "EmailVerified" policy wiring (reflection-based) ──
    //
    // ThumbnailTests.cs had no pre-existing reflection-based [Authorize] test to replace
    // (unlike UploadUrlTests.cs) — this is purely new coverage, mirroring
    // UploadUrlTests.CreateUploadUrl_HasEmailVerifiedPolicy's own reasoning: the authorization
    // middleware that actually produces 401/403 doesn't run when an action is invoked directly
    // in a unit test, so this asserts the attribute/policy name are correctly wired instead of
    // simulating the pipeline (AuctionService.IntegrationTests/EmailVerifiedPolicyTests.cs's job).
    [Fact]
    public void CreateThumbnail_HasEmailVerifiedPolicy()
    {
        var method = typeof(AuctionsController).GetMethod(
            nameof(AuctionsController.CreateThumbnail), [typeof(ThumbnailRequest)]);

        Assert.NotNull(method);

        var attribute = method!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("EmailVerified", attribute!.Policy);
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
