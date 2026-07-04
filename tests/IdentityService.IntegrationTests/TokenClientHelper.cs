using System.Text.Json;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Requests a user-bound access token from the real <c>/connect/token</c> endpoint using the
/// test-only Resource Owner Password Credentials client (see
/// <see cref="CustomWebAppFactory"/>'s remarks for why ROPC, not the browser-interactive
/// `webapp` client, is used here). Shared by <see cref="TokenEndpointTests"/> (which asserts
/// on the token endpoint's own response/claims) and <see cref="ProtectedEndpointTests"/>
/// (which just needs a valid token to present elsewhere).
/// </summary>
internal static class TokenClientHelper
{
    public static async Task<(string AccessToken, string RawResponseBody)> RequestPasswordGrantTokenAsync(
        HttpClient client, string username, string password, CancellationToken ct)
    {
        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password,
                ["client_id"] = CustomWebAppFactory.TestClientId,
                ["client_secret"] = CustomWebAppFactory.TestClientSecret,
                ["scope"] = "openid profile apexautobid",
            }),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Token request failed with {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        return (accessToken, body);
    }
}
