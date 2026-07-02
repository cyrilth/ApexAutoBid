namespace SearchService.Domain.Models;

/// <summary>
/// Named constants for <c>GET api/search</c> shared between the Application layer
/// (parameter validation/defaults in <c>SearchAppService</c>) and Infrastructure
/// (predicate construction in <c>ItemRepository.SearchAsync</c>), so both sides agree on a
/// single source of truth instead of duplicating magic numbers.
/// </summary>
public static class ItemSearchDefaults
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 50;

    /// <summary>
    /// The window used by <see cref="Enums.ItemFilterBy.EndingSoon"/>: an item counts as
    /// "ending soon" when it is Live, AuctionEnd is still in the future, and AuctionEnd
    /// falls within this many hours of "now".
    /// </summary>
    public static readonly TimeSpan EndingSoonWindow = TimeSpan.FromHours(6);
}
