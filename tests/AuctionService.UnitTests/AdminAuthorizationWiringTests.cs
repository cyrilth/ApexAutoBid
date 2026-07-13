using AuctionService.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Wiring-level unit tests (Phase 11 Task 3) mirroring
/// <c>AuctionsControllerTests.AssertHasEmailVerifiedPolicy</c>'s own pattern: [Authorize] (and
/// its Policy) is what the ASP.NET Core authorization middleware reads to produce 401/403 —
/// that middleware does not run when a controller class is merely reflected over, so these
/// tests assert the attribute/policy name are correctly wired on every new admin controller.
/// The actual pipeline-level 401 (anonymous)/403 (non-admin) behavior is covered by
/// AuctionService.IntegrationTests/AdminAuthorizationTests.cs (real pipeline).
/// </summary>
public class AdminAuthorizationWiringTests
{
    [Theory]
    [InlineData(typeof(AdminAuctionsController))]
    [InlineData(typeof(AdminBannersController))]
    [InlineData(typeof(AdminSettingsController))]
    public void AdminController_HasAdminOnlyPolicyAtClassLevel(Type controllerType)
    {
        var attribute = controllerType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("AdminOnly", attribute!.Policy);
    }

    // The public banner-read controller must NOT require the admin policy — it is deliberately
    // anonymous (Requirements §10.3).
    [Fact]
    public void BannersController_HasNoAuthorizeAttribute()
    {
        var attribute = typeof(BannersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.Null(attribute);
    }
}
