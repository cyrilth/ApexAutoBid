using SearchService.Application.DTOs;

namespace SearchService.Application.Services;

/// <summary>
/// Result codes for <see cref="ISearchService.SearchAsync"/> so the controller can map
/// outcomes to HTTP status codes without the Application layer having any knowledge of HTTP.
/// Mirrors the <c>(outcome enum, response)</c> pattern used by
/// <c>AuctionService.Application.Services.IAuctionImageService</c>.
/// </summary>
public enum SearchOutcome
{
    Success,

    /// <summary>orderBy did not match any of the allowed keywords (case-insensitive).</summary>
    InvalidOrderBy,

    /// <summary>filterBy did not match any of the allowed keywords (case-insensitive).</summary>
    InvalidFilterBy,

    /// <summary>pageNumber was supplied and was less than 1.</summary>
    InvalidPageNumber,

    /// <summary>pageSize was supplied and was outside the 1–50 bound.</summary>
    InvalidPageSize
}

/// <summary>
/// Application-level service for <c>GET api/search</c> (Requirements §3.2). Controllers
/// depend only on this interface — never on <c>IItemRepository</c> or any Infrastructure
/// type.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Validates and normalizes <paramref name="request"/>, then returns a page of matching
    /// items. Returns a non-<see cref="SearchOutcome.Success"/> outcome (and a null result)
    /// for any invalid parameter rather than silently falling back to a default — surprising
    /// API behavior is avoided at the cost of a 400.
    /// </summary>
    Task<(SearchOutcome Outcome, SearchResultDto? Result)> SearchAsync(
        SearchParamsDto request, CancellationToken cancellationToken);
}
