using MapsterMapper;
using SearchService.Application.DTOs;
using SearchService.Domain.Enums;
using SearchService.Domain.Interfaces;
using SearchService.Domain.Models;

namespace SearchService.Application.Services;

/// <summary>
/// <see cref="ISearchService"/> implementation for <c>GET api/search</c>. Validates and
/// normalizes the raw <see cref="SearchParamsDto"/>, builds a Domain
/// <see cref="ItemSearchQuery"/>, delegates to <see cref="IItemRepository.SearchAsync"/>, and
/// maps the Domain result to <see cref="SearchResultDto"/>.
/// </summary>
public class SearchAppService(
    IItemRepository repository,
    IMapper mapper,
    TimeProvider timeProvider) : ISearchService
{
    /// <summary>
    /// Defensive cap on an anonymous endpoint — an overlong searchTerm is truncated, not
    /// rejected with a 400 (it isn't a client error worth failing the whole request for).
    /// </summary>
    private const int MaxSearchTermLength = 200;

    public async Task<(SearchOutcome Outcome, SearchResultDto? Result)> SearchAsync(
        SearchParamsDto request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber ?? ItemSearchDefaults.DefaultPageNumber;
        if (pageNumber < 1)
            return (SearchOutcome.InvalidPageNumber, null);

        var pageSize = request.PageSize ?? ItemSearchDefaults.DefaultPageSize;
        if (pageSize is < 1 or > ItemSearchDefaults.MaxPageSize)
            return (SearchOutcome.InvalidPageSize, null);

        if (!TryParseOrderBy(request.OrderBy, out var orderBy))
            return (SearchOutcome.InvalidOrderBy, null);

        if (!TryParseFilterBy(request.FilterBy, out var filterBy))
            return (SearchOutcome.InvalidFilterBy, null);

        var query = new ItemSearchQuery
        {
            SearchTerm = SanitizeSearchTerm(request.SearchTerm),
            // Whitespace-only Seller/Winner normalize to null (no filter) rather than an
            // empty-string exact-match, which would match nothing (Item.Seller/Winner are
            // never empty strings) and silently return zero results for a value the caller
            // almost certainly meant to omit.
            Seller = NullIfWhiteSpace(request.Seller),
            Winner = NullIfWhiteSpace(request.Winner),
            OrderBy = orderBy,
            FilterBy = filterBy,
            // Injected TimeProvider (registered as TimeProvider.System in
            // ApplicationServiceExtensions) rather than DateTime.UtcNow, so the
            // EndingSoon/Finished filter unit tests (Phase 2 Task 9) can supply a fake clock.
            Now = timeProvider.GetUtcNow().UtcDateTime,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var paged = await repository.SearchAsync(query, cancellationToken);

        var result = new SearchResultDto
        {
            Results = mapper.Map<List<ItemDto>>(paged.Results),
            TotalCount = paged.TotalCount,
            PageCount = paged.PageCount
        };

        return (SearchOutcome.Success, result);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Neutralizes MongoDB <c>$text</c> search-string grammar in free-form user input before
    /// it ever reaches <c>ItemRepository.SearchAsync</c>'s <c>.Match(Search.Full, term)</c>.
    /// </summary>
    /// <remarks>
    /// Mongo's <c>$text</c> operator treats a token with a leading <c>-</c> as an
    /// <b>exclusion</b> (e.g. <c>-ford</c> means "NOT ford") and a <c>"..."</c>-quoted span
    /// as an <b>exact phrase</b>, not literal characters to search for. On a public anonymous
    /// endpoint, a user innocently typing <c>-ford</c> or a term containing a quote must not
    /// silently flip the query into "everything except ford" or phrase-matching — so every
    /// <c>"</c> is stripped outright, and a leading <c>-</c> is stripped from each
    /// whitespace-delimited token individually (NOT from the term as a whole, and the result
    /// is NOT re-wrapped in quotes) so Mongo's default OR-of-terms behavior for multi-word
    /// queries is preserved.
    /// </remarks>
    private static string? SanitizeSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var truncated = value.Length > MaxSearchTermLength ? value[..MaxSearchTermLength] : value;

        var tokens = truncated
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Replace("\"", string.Empty).TrimStart('-'))
            .Where(token => token.Length > 0);

        var sanitized = string.Join(' ', tokens);

        return sanitized.Length > 0 ? sanitized : null;
    }

    private static bool TryParseOrderBy(string? value, out ItemOrderBy orderBy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            orderBy = ItemOrderBy.EndingSoon;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "endingsoon":
                orderBy = ItemOrderBy.EndingSoon;
                return true;
            case "make":
                orderBy = ItemOrderBy.Make;
                return true;
            case "new":
                orderBy = ItemOrderBy.New;
                return true;
            default:
                orderBy = default;
                return false;
        }
    }

    private static bool TryParseFilterBy(string? value, out ItemFilterBy filterBy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Omitted filterBy = no lifecycle constraint (ItemFilterBy.All), not "live" — see
            // ItemFilterBy.All's XML doc for why.
            filterBy = ItemFilterBy.All;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "all":
                filterBy = ItemFilterBy.All;
                return true;
            case "live":
                filterBy = ItemFilterBy.Live;
                return true;
            case "endingsoon":
                filterBy = ItemFilterBy.EndingSoon;
                return true;
            case "finished":
                filterBy = ItemFilterBy.Finished;
                return true;
            default:
                filterBy = default;
                return false;
        }
    }
}
