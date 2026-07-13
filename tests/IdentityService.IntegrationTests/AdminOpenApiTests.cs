using System.Net;
using Xunit;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Smoke tests for the admin API's OpenAPI document + Scalar UI (Phase 11 Task 2.8) — confirms
/// <c>AddOpenApi()</c>/<c>MapOpenApi()</c>/<c>MapScalarApiReference()</c> (HostingExtensions.cs)
/// actually serve without error and that the generated document describes
/// <c>AdminUsersController</c>'s operations (not just an empty/default document).
/// </summary>
[Collection(IdentityServiceApiCollection.Name)]
public class AdminOpenApiTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task OpenApiDocument_DescribesAdminUsersEndpoints()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/admin/users", body);
        Assert.Contains("\"Bearer\"", body);
    }

    [Fact]
    public async Task ScalarUi_Returns200()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
