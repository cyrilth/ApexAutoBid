using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Integration tests for <c>AdminUsersController</c>'s "AdminOnly" authorization gate (Phase 11
/// Task 2 / Requirements.md §10: "every admin endpoint returns 403 for non-admin callers") —
/// exercised against the REAL JWT bearer authentication/authorization pipeline
/// (<see cref="CustomWebAppFactory"/>), which a unit test cannot do without a live HTTP host.
/// <para>
/// Every <c>api/admin/users*</c> endpoint is checked twice: once with NO bearer token (expect
/// 401 — authentication itself fails) and once as the seeded non-admin user "bob" (expect 403 —
/// authenticated, but missing the "admin" role). Happy-path/audit-entry coverage for the admin
/// operations themselves lives in <c>IdentityService.UnitTests/AdminUserServiceTests.cs</c>.
/// </para>
/// </summary>
[Collection(IdentityServiceApiCollection.Name)]
public class AdminUsersAuthorizationTests(CustomWebAppFactory factory)
{
    private static readonly (HttpMethod Method, string Path)[] AdminEndpoints =
    [
        (HttpMethod.Get, "/api/admin/users"),
        (HttpMethod.Get, "/api/admin/users/stats"),
        (HttpMethod.Post, "/api/admin/users"),
        (HttpMethod.Post, $"/api/admin/users/{Guid.Empty}/reset-password"),
        (HttpMethod.Post, $"/api/admin/users/{Guid.Empty}/resend-confirmation"),
        (HttpMethod.Put, $"/api/admin/users/{Guid.Empty}/roles"),
        (HttpMethod.Put, $"/api/admin/users/{Guid.Empty}/lock"),
    ];

    public static IEnumerable<object[]> AdminEndpointCases() =>
        AdminEndpoints.Select(e => new object[] { e.Method, e.Path });

    [Theory]
    [MemberData(nameof(AdminEndpointCases))]
    public async Task AdminEndpoint_NoBearerToken_Returns401(HttpMethod method, string path)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminEndpointCases))]
    public async Task AdminEndpoint_NonAdminBearerToken_Returns403(HttpMethod method, string path)
    {
        var client = factory.CreateClient();
        // "bob" is one of the Requirements.md §8.1 seeded dev users and holds no roles
        // (only "admin" does — SeedData.cs).
        var (accessToken, _) = await TokenClientHelper.RequestPasswordGrantTokenAsync(
            client, "bob", "Pass123$", TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Regression guard: the seeded "admin" user IS allowed through the gate ────────────
    [Fact]
    public async Task GetUsers_AdminBearerToken_Returns200()
    {
        var client = factory.CreateClient();
        var (accessToken, _) = await TokenClientHelper.RequestPasswordGrantTokenAsync(
            client, "admin", "Pass123$", TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_AdminBearerToken_Returns200()
    {
        var client = factory.CreateClient();
        var (accessToken, _) = await TokenClientHelper.RequestPasswordGrantTokenAsync(
            client, "admin", "Pass123$", TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
