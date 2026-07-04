using System.Text.Json;

namespace IdentityService.IntegrationTests;

/// <summary>
/// Decodes a JWT's header/payload segments for structural assertions (claim names/values),
/// without validating the signature — this is an integration test against a real, freshly
/// signed token from the real token endpoint, so signature validity is implicit in the token
/// having been minted at all; these tests care about what's IN it. Hand-rolled rather than
/// pulling in a JWT-parsing package: base64url + System.Text.Json (already in the BCL) is all
/// that's needed to read two JSON objects.
/// </summary>
internal static class JwtParsingHelper
{
    public static JsonElement DecodeHeader(string jwt) => DecodeSegment(jwt, 0);

    public static JsonElement DecodePayload(string jwt) => DecodeSegment(jwt, 1);

    private static JsonElement DecodeSegment(string jwt, int segmentIndex)
    {
        var segment = jwt.Split('.')[segmentIndex];
        var padded = segment.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var json = Convert.FromBase64String(padded);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
