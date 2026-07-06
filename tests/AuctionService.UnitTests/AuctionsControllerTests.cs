using System.Reflection;
using System.Security.Claims;
using AuctionService.API.Controllers;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
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
/// controller's <c>User.Identity!.Name</c> resolves when the action methods are invoked
/// directly.
/// <para>
/// Phase 3 Task 19: CreateAuction/UpdateAuction/DeleteAuction no longer perform their own
/// in-body <c>email_verified</c> check (that ad-hoc check — and its unit-testability via direct
/// action invocation — is exactly what this task replaced) — enforcement now lives entirely in
/// the "EmailVerified" authorization POLICY (Program.cs), which the ASP.NET Core authorization
/// middleware evaluates before an action method ever runs. That middleware does not execute when
/// a test calls a controller method directly (as every test in this class does), so the
/// email_verified/401/403 behavior itself is covered by
/// AuctionService.IntegrationTests/EmailVerifiedPolicyTests.cs (real pipeline) instead; this
/// class instead asserts the WIRING — that the attribute and policy name are actually present
/// on each method — mirroring the existing reflection-based
/// UploadUrlTests.CreateUploadUrl_HasAuthorizeAttribute pattern, one level more precise (checking
/// the specific policy name, not just attribute presence).
/// </para>
/// </summary>
public class AuctionsControllerTests
{
    private readonly IAuctionService _service = Substitute.For<IAuctionService>();
    private readonly IAuctionImageService _imageService = Substitute.For<IAuctionImageService>();

    // Builds the controller under test with the substituted service and an authenticated user.
    // ClaimsIdentity defaults its NameClaimType to ClaimTypes.Name, so User.Identity!.Name
    // resolves to the username claim added below. email_verified is always "true" here — a
    // realistic shape for a principal that would actually reach these action bodies in
    // production (the "EmailVerified" policy already rejected anything else before the action
    // runs) — no test in this class needs a "false" variant since the ad-hoc check that used to
    // read this claim itself is gone (see this class's own remarks).
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

    // ── Phase 3 Task 19 — "EmailVerified" policy wiring (reflection-based) ───────
    //
    // Mirrors UploadUrlTests.CreateUploadUrl_HasAuthorizeAttribute's own comment: [Authorize]
    // (and, here, its Policy) is what the ASP.NET Core authorization middleware reads to
    // produce 401/403 — that middleware does not run when an action is invoked directly in a
    // unit test, so these tests assert the attribute/policy name are correctly wired rather than
    // simulating the pipeline (that's AuctionService.IntegrationTests/EmailVerifiedPolicyTests.cs's job).
    [Fact]
    public void CreateAuction_HasEmailVerifiedPolicy()
    {
        var method = typeof(AuctionsController).GetMethod(
            nameof(AuctionsController.CreateAuction), [typeof(CreateAuctionDto)]);

        AssertHasEmailVerifiedPolicy(method);
    }

    [Fact]
    public void UpdateAuction_HasEmailVerifiedPolicy()
    {
        var method = typeof(AuctionsController).GetMethod(
            nameof(AuctionsController.UpdateAuction), [typeof(Guid), typeof(UpdateAuctionDto)]);

        AssertHasEmailVerifiedPolicy(method);
    }

    [Fact]
    public void DeleteAuction_HasEmailVerifiedPolicy()
    {
        var method = typeof(AuctionsController).GetMethod(
            nameof(AuctionsController.DeleteAuction), [typeof(Guid)]);

        AssertHasEmailVerifiedPolicy(method);
    }

    private static void AssertHasEmailVerifiedPolicy(MethodInfo? method)
    {
        Assert.NotNull(method);

        var attribute = method!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("EmailVerified", attribute!.Policy);
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

    // ── 8.3  GetAllAuctions(?date=) — UTC-normalized date filtering ──────────────
    // Regression guard for the date-parsing fix: an offset-bearing ?date must be converted to
    // the correct UTC instant (AdjustToUniversal), not relabelled with SpecifyKind — which would
    // mislabel a local-converted value as UTC on a non-UTC host. Note: on a UTC host the old and
    // new code converge, so this locks in the correct contract rather than failing on the old path.
    [Fact]
    public async Task GetAllAuctions_WhenDateHasOffset_PassesConvertedUtcInstantToService()
    {
        DateTime? captured = null;
        _service.GetAuctionsAsync(Arg.Do<DateTime?>(d => captured = d))
            .Returns(new List<AuctionDto>());
        var controller = BuildController();

        // 2026-07-01T00:00:00+05:00 is the instant 2026-06-30T19:00:00Z.
        await controller.GetAllAuctions("2026-07-01T00:00:00+05:00");

        Assert.NotNull(captured);
        Assert.Equal(DateTimeKind.Utc, captured!.Value.Kind);
        Assert.Equal(new DateTime(2026, 6, 30, 19, 0, 0, DateTimeKind.Utc), captured.Value);
    }

    [Fact]
    public async Task GetAllAuctions_WhenNoDate_PassesNullToService()
    {
        DateTime? captured = new DateTime(2000, 1, 1);
        _service.GetAuctionsAsync(Arg.Do<DateTime?>(d => captured = d))
            .Returns(new List<AuctionDto>());
        var controller = BuildController();

        await controller.GetAllAuctions(null);

        Assert.Null(captured);
    }

    [Fact]
    public async Task GetAllAuctions_WhenDateInvalid_Returns400BadRequest()
    {
        var controller = BuildController();

        var result = await controller.GetAllAuctions("not-a-date");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── 19.1  GetAuctionById — forwards the caller's username to the service ────
    //
    // The actual post-sale contact-exchange redaction logic lives in
    // AuctionAppService.GetAuctionByIdAsync (covered by PostSaleContactExchangeTests.cs); these
    // tests only assert the controller's WIRING — that it correctly derives requestingUser from
    // User.Identity?.Name (null for an unauthenticated/anonymous principal) and forwards it
    // unchanged, and that a null service result becomes a 404.

    [Fact]
    public async Task GetAuctionById_WhenAuthenticated_PassesUsernameToService()
    {
        string? captured = "not-yet-set";
        _service.GetAuctionByIdAsync(Arg.Any<Guid>(), Arg.Do<string?>(u => captured = u))
            .Returns((AuctionDetailDto?)null);
        var controller = BuildController(username: "seller-bob");

        await controller.GetAuctionById(Guid.NewGuid());

        Assert.Equal("seller-bob", captured);
    }

    [Fact]
    public async Task GetAuctionById_WhenAnonymous_PassesNullToService()
    {
        string? captured = "not-yet-set";
        _service.GetAuctionByIdAsync(Arg.Any<Guid>(), Arg.Do<string?>(u => captured = u))
            .Returns((AuctionDetailDto?)null);

        // Empty ClaimsIdentity() defaults to IsAuthenticated = false (no AuthenticationType) —
        // User.Identity.Name is null, mirroring an anonymous caller with no bearer token (both
        // the real JwtBearer handler and TestAuthHandler leave User in this shape for such a
        // request — see TestAuthHandler's own remarks in AuctionService.IntegrationTests).
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var controller = new AuctionsController(_service, _imageService, NullLogger<AuctionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = anonymousUser },
            },
        };

        await controller.GetAuctionById(Guid.NewGuid());

        Assert.Null(captured);
    }

    [Fact]
    public async Task GetAuctionById_WhenNotFound_Returns404()
    {
        _service.GetAuctionByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>())
            .Returns((AuctionDetailDto?)null);
        var controller = BuildController();

        var result = await controller.GetAuctionById(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

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
