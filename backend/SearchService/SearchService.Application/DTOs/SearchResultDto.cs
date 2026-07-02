namespace SearchService.Application.DTOs;

/// <summary>Paged response for <c>GET api/search</c>.</summary>
public class SearchResultDto
{
    public required List<ItemDto> Results { get; init; }
    public required long TotalCount { get; init; }
    public required int PageCount { get; init; }
}
