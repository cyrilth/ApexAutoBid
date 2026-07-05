using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// Calls Cloudflare Turnstile's <c>siteverify</c> API over a plain typed <see cref="HttpClient"/>
/// (Phase 3 Task 16.1 — Requirements.md §3.4 explicitly calls for "plain HttpClient, no NuGet
/// package needed"; registered via <c>AddHttpClient&lt;ITurnstileValidator, TurnstileValidator&gt;()</c>
/// in HostingExtensions.cs, so <see cref="HttpClient"/> here comes from
/// <see cref="IHttpClientFactory"/>, not a bare <c>new HttpClient()</c>).
/// <para>
/// Never logs the response token or the secret key (Requirements.md §13.5) — only Cloudflare's
/// own <c>error-codes</c> strings, which are documented, non-sensitive codes like
/// "invalid-input-response" or "timeout-or-duplicate", never anything user-supplied verbatim.
/// </para>
/// <para>
/// Fails CLOSED on any network/deserialization problem (returns <see langword="false"/>, i.e.
/// "not verified") rather than failing open — Turnstile's whole purpose is blocking automated
/// registration; treating an unreachable Cloudflare as "let it through" would silently disable
/// bot protection during exactly the kind of outage an attacker might exploit or simply benefit
/// from by coincidence. The cost of this choice is that a genuine Cloudflare outage blocks
/// legitimate registrations too — an accepted tradeoff, not an oversight (registration is
/// retryable; a stray fail-open bot-protection outage should not be a completely open door).
/// </para>
/// </summary>
public class TurnstileValidator(HttpClient httpClient, IOptions<TurnstileOptions> options, ILogger<TurnstileValidator> logger)
    : ITurnstileValidator
{
    private readonly TurnstileOptions _options = options.Value;

    public async Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        // Defense-in-depth re-check — Register/Index.cshtml.cs already rejects a missing/empty
        // token before ever calling this, specifically to avoid burning a siteverify call
        // (Task 16.1's explicit instruction), but this is a public interface method other
        // callers could reach without that guard.
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await httpClient.PostAsync(
                _options.VerifyUrl, new FormUrlEncodedContent(form), ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            if (result is null)
            {
                logger.LogWarning("Turnstile siteverify returned an unparseable response");
                return false;
            }

            if (!result.Success)
            {
                logger.LogWarning(
                    "Turnstile validation failed with error code(s) {ErrorCodes}",
                    string.Join(", ", result.ErrorCodes ?? []));
            }

            return result.Success;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Turnstile siteverify call failed");
            return false;
        }
    }

    // Only the two fields this app actually needs — Cloudflare's real response also carries
    // challenge_ts/hostname/action/cdata, none of which anything here reads.
    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
