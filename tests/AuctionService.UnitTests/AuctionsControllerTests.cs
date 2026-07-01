using System.Security.Claims;
using AuctionService.API.Controllers;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AuctionsController"/> write actions (Phase 1 Task 14).
/// Each test substitutes <see cref="IAuctionService"/> and asserts the controller maps the
/// service outcome to the correct HTTP result. A ClaimsPrincipal is attached so the
/// controller's <c>User.Identity!.Name</c> (and the <c>email_verified</c> gate on create)
/// resolve when the action methods are invoked directly.
/// </summary>
public class AuctionsControllerTests
{
    private readonly IAuctionService _service = Substitute.For<IAuctionService>();
    private readonly IAuctionImageService _imageService = Substitute.For<IAuctionImageService>();

    // Builds the controller under test with the substituted service and an authenticated user.
    // ClaimsIdentity defaults its NameClaimType to ClaimTypes.Name, so User.Identity!.Name
    // resolves to the username claim added below.
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

    private static CreateAuctionDto SampleCreateDto() => new()
    {
        Make = "Ford",
        Model = "GT",
        Color = "Red",
        Mileage = 1000,
        Year = 2020,
        ReservePrice = 20000,
        Images = [new ImageDto { Url = "http://images.local/ford-gt.jpg", SortOrder = 0 }],
        AuctionEnd = DateTime.UtcNow.AddDays(7),
    };

    // ── 14.1  CreateAuction — failed save returns 400 ────────────────────────────
    [Fact]
    public async Task CreateAuction_WhenSaveFails_Returns400BadRequest()
    {
        _service.CreateAuctionAsync(Arg.Any<CreateAuctionDto>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(new AuctionCreateResult(AuctionWriteResult.SaveFailed, null));
        var controller = BuildController();

        var result = await controller.CreateAuction(SampleCreateDto());

        // CreateAuction returns ActionResult<AuctionDto>; the produced result is in .Result.
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── 14.2  UpdateAuction — valid DTO returns OK ───────────────────────────────
    [Fact]
    public async Task UpdateAuction_WhenSuccess_ReturnsOk()
    {
        _service.UpdateAuctionAsync(Arg.Any<Guid>(), Arg.Any<UpdateAuctionDto>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.Success);
        var controller = BuildController();

        var result = await controller.UpdateAuction(Guid.NewGuid(), new UpdateAuctionDto { Make = "Ferrari" });

        Assert.IsType<OkResult>(result);
    }

    // ── 14.3  UpdateAuction — invalid user returns 403 ───────────────────────────
    [Fact]
    public async Task UpdateAuction_WhenForbidden_Returns403Forbid()
    {
        _service.UpdateAuctionAsync(Arg.Any<Guid>(), Arg.Any<UpdateAuctionDto>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.Forbidden);
        var controller = BuildController();

        var result = await controller.UpdateAuction(Guid.NewGuid(), new UpdateAuctionDto());

        Assert.IsType<ForbidResult>(result);
    }

    // ── 14.4  UpdateAuction — unknown id returns 404 ─────────────────────────────
    [Fact]
    public async Task UpdateAuction_WhenNotFound_Returns404()
    {
        _service.UpdateAuctionAsync(Arg.Any<Guid>(), Arg.Any<UpdateAuctionDto>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.NotFound);
        var controller = BuildController();

        var result = await controller.UpdateAuction(Guid.NewGuid(), new UpdateAuctionDto());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── 14.5  DeleteAuction — valid user returns OK ──────────────────────────────
    [Fact]
    public async Task DeleteAuction_WhenSuccess_ReturnsOk()
    {
        _service.DeleteAuctionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.Success);
        var controller = BuildController();

        var result = await controller.DeleteAuction(Guid.NewGuid());

        Assert.IsType<OkResult>(result);
    }

    // ── 14.6  DeleteAuction — unknown id returns 404 ─────────────────────────────
    [Fact]
    public async Task DeleteAuction_WhenNotFound_Returns404()
    {
        _service.DeleteAuctionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.NotFound);
        var controller = BuildController();

        var result = await controller.DeleteAuction(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── 14.7  DeleteAuction — invalid user returns 403 ───────────────────────────
    [Fact]
    public async Task DeleteAuction_WhenForbidden_Returns403Forbid()
    {
        _service.DeleteAuctionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(AuctionWriteResult.Forbidden);
        var controller = BuildController();

        var result = await controller.DeleteAuction(Guid.NewGuid());

        Assert.IsType<ForbidResult>(result);
    }
}
