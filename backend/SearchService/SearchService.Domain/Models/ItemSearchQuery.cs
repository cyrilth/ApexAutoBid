using SearchService.Domain.Enums;

namespace SearchService.Domain.Models;

/// <summary>
/// Pure query parameters for <c>IItemRepository.SearchAsync</c>. Built entirely in the
/// Application layer (<c>SearchAppService</c>) from validated/normalized request input —
/// Domain and Infrastructure never see raw, unvalidated query-string strings.
/// </summary>
/// <remarks>
/// <b>Why <see cref="Now"/> travels in the query instead of Infrastructure reading the wall
/// clock:</b> the Application layer resolves "now" exactly once via the injected
/// <c>TimeProvider</c> (<c>TimeProvider.GetUtcNow().UtcDateTime</c>), which is what makes the
/// <see cref="ItemFilterBy.EndingSoon"/>/<see cref="ItemFilterBy.Finished"/> filter unit tests
/// (Phase 2 Task 9) deterministic. Infrastructure stays a pure translator of
/// <see cref="FilterBy"/> + <see cref="Now"/> into a MongoDB predicate and is never
/// itself time-aware.
/// </remarks>
public sealed class ItemSearchQuery
{
    /// <summary>Free-text term matched against the Make/Model/Color text index. Null = no text filter.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>Exact-match filter on <c>Item.Seller</c>. Null = no seller filter.</summary>
    public string? Seller { get; init; }

    /// <summary>Exact-match filter on <c>Item.Winner</c>. Null = no winner filter.</summary>
    public string? Winner { get; init; }

    public ItemOrderBy OrderBy { get; init; } = ItemOrderBy.EndingSoon;

    public ItemFilterBy FilterBy { get; init; } = ItemFilterBy.All;

    /// <summary>The Application layer's resolved "now", used to evaluate <see cref="FilterBy"/>.</summary>
    public required DateTime Now { get; init; }

    /// <summary>1-based page number. Caller-validated to be &gt;= 1.</summary>
    public int PageNumber { get; init; } = ItemSearchDefaults.DefaultPageNumber;

    /// <summary>Caller-validated to be between 1 and <see cref="ItemSearchDefaults.MaxPageSize"/>.</summary>
    public int PageSize { get; init; } = ItemSearchDefaults.DefaultPageSize;
}
