using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using SearchService.Application.DTOs;
using SearchService.Application.Services;

namespace SearchService.Infrastructure.Http;

/// <summary>
/// <see cref="IAuctionServiceClient"/> implementation — a typed <c>HttpClient</c> against the
/// Auction Service's <c>GET api/auctions[?date=]</c> endpoint. Registered in
/// <c>InfrastructureServiceExtensions</c> via <c>AddHttpClient&lt;...&gt;()</c> with
/// <c>AddStandardResilienceHandler()</c> (retries with exponential backoff, a circuit
/// breaker, and per-attempt/per-request timeouts — Requirements §3.2's "Microsoft.Extensions.
/// Http.Resilience (Polly v8)").
/// </summary>
public sealed class AuctionServiceHttpClient(
    HttpClient httpClient, ILogger<AuctionServiceHttpClient> logger) : IAuctionServiceClient
{
    // JsonSerializerDefaults.Web sets camelCase property naming AND case-insensitive property
    // matching. AuctionService.API's controllers serialize with ASP.NET Core's default
    // camelCase output (confirmed empirically — GET api/search's own JSON responses are
    // camelCase too), while AuctionSyncDto's properties are PascalCase; without this, every
    // property would silently fail to bind (defaulting to null/0) rather than throwing —
    // System.Text.Json's plain default options are case-sensitive PascalCase-only.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<AuctionSyncDto>?> GetAuctionsFromDateAsync(
        DateTime? updatedAfter, CancellationToken cancellationToken)
    {
        // "o" — ISO 8601 round-trip format, UTC. AuctionsController.GetAllAuctions parses the
        // date query param with AssumeUniversal | AdjustToUniversal, so this must always be
        // an unambiguous, explicitly-UTC instant.
        //
        // Deliberately NO leading '/' on these relative URIs — pairs with
        // InfrastructureServiceExtensions normalizing HttpClient.BaseAddress to always end in
        // '/', so a base URL with a sub-path resolves correctly (see that comment for why).
        var requestUri = updatedAfter is { } date
            ? $"api/auctions?date={Uri.EscapeDataString(date.ToString("o", CultureInfo.InvariantCulture))}"
            : "api/auctions";

        try
        {
            var auctions = await httpClient.GetFromJsonAsync<List<AuctionSyncDto>>(
                requestUri, JsonOptions, cancellationToken);

            return auctions ?? [];
        }
        catch (Exception ex) when (
            ex is HttpRequestException or ExecutionRejectedException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // By the time this is caught, AddStandardResilienceHandler's retries + circuit
            // breaker + timeouts have already been exhausted — this is a sustained failure to
            // reach the Auction Service, not a single transient blip.
            //
            // ExecutionRejectedException (Polly v8's common base type) is caught rather than
            // naming TimeoutRejectedException/BrokenCircuitException individually: the
            // standard resilience pipeline is actually five stages — rate limiter, total
            // timeout, retry, circuit breaker, attempt timeout — and a rate-limiter rejection
            // throws Polly.RateLimiting.RateLimiterRejectedException, a sibling that also
            // derives from this same base. Naming only the two most obvious derived types
            // would leave that one to escape uncaught and break this method's "never throws
            // for a transient failure" contract; catching the base covers all of them,
            // present and future.
            //
            // The OperationCanceledException/TaskCanceledException branch guards against the
            // resilience pipeline's own per-attempt timeout (which manifests as a
            // cancellation) while still letting a caller-initiated cancellation (this
            // method's own cancellationToken) propagate untouched rather than being
            // misreported as "unreachable". In practice this branch is currently vacuous —
            // the sole caller (DataSyncService) always passes CancellationToken.None, so
            // cancellationToken.IsCancellationRequested can never be true today — kept as
            // correct future-proofing, not a tested path.
            logger.LogError(ex, "Failed to reach Auction Service at {RequestUri} after retries", requestUri);
            return null;
        }
    }
}
