using BiddingService.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AdminBidsController"/>'s admin-role wiring (Phase 11 Task 9.1 —
/// "admin endpoints return 403 for non-admin callers"). Reflection-based, exactly like
/// <c>AuctionService.UnitTests.UploadUrlTests</c>' identical "EmailVerified" policy-wiring
/// convention: the ASP.NET Core authorization middleware (which is what actually produces the
/// 401/403 response) does not run when a controller is instantiated and invoked directly in a
/// unit test, so this asserts the <see cref="AuthorizeAttribute"/> and its exact <c>Roles</c>
/// value are correctly wired on every admin action, rather than simulating the whole pipeline —
/// simulating the pipeline end-to-end (a real anonymous/non-admin/admin HTTP call) is an
/// integration-test concern.
/// </summary>
public class AdminBidsControllerAuthorizationTests
{
    [Fact]
    public void AdminBidsController_IsDecoratedWithAdminRoleAuthorization()
    {
        var attribute = typeof(AdminBidsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("admin", attribute!.Roles);
    }

    [Theory]
    [InlineData(nameof(AdminBidsController.RemoveBid))]
    [InlineData(nameof(AdminBidsController.GetStats))]
    public void AdminBidsController_EveryAction_InheritsTheControllerLevelAdminRoleAuthorization(
        string actionName)
    {
        // No [AllowAnonymous] escape hatch on any action — every api/admin/bids/* endpoint must
        // require the admin role, none of them may opt back out to anonymous/authenticated-only.
        var method = typeof(AdminBidsController).GetMethods()
            .Single(m => m.Name == actionName && m.DeclaringType == typeof(AdminBidsController));

        var allowAnonymous = method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true);

        Assert.Empty(allowAnonymous);

        // The controller-level [Authorize(Roles = "admin")] attribute (asserted above) applies
        // to every action that doesn't override it — confirmed here by checking the type itself
        // still carries it (inherited, not shadowed) rather than assuming.
        var controllerAttribute = typeof(AdminBidsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal("admin", controllerAttribute.Roles);
    }
}
