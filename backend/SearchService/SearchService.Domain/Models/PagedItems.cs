using SearchService.Domain.Entities;

namespace SearchService.Domain.Models;

/// <summary>
/// A single page of <see cref="Item"/> results plus paging metadata, returned by
/// <c>IItemRepository.SearchAsync</c>.
/// </summary>
public sealed class PagedItems
{
    public required IReadOnlyList<Item> Results { get; init; }
    public required long TotalCount { get; init; }
    public required int PageCount { get; init; }
}
