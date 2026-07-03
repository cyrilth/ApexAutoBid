using SearchService.Application.DTOs;

namespace SearchService.Application.Services;

/// <summary>
/// HTTP client abstraction for the Auction Service's <c>GET api/auctions[?date=]</c>
/// endpoint (Phase 2 Task 6 HTTP polling fallback). Implemented in Infrastructure
/// (<c>AuctionServiceHttpClient</c>); Application depends only on this interface — never on
/// <c>HttpClient</c> or any resilience package directly.
/// </summary>
public interface IAuctionServiceClient
{
    /// <summary>
    /// Returns auctions with <c>UpdatedAt</c> strictly greater than
    /// <paramref name="updatedAfter"/> (every auction when <see langword="null"/>), or
    /// <see langword="null"/> if the Auction Service could not be reached after the
    /// resilience pipeline's retries/circuit breaker/timeout were exhausted. Never throws
    /// for a transient/network/HTTP failure — see <c>DataSyncService</c>'s XML doc for the
    /// startup failure policy this return-null contract supports.
    /// </summary>
    Task<List<AuctionSyncDto>?> GetAuctionsFromDateAsync(
        DateTime? updatedAfter, CancellationToken cancellationToken);
}
