namespace SearchService.Domain.Enums;

/// <summary>
/// Lifecycle filter options for <c>GET api/search</c> (Requirements §3.2 <c>filterBy</c> param).
/// </summary>
public enum ItemFilterBy
{
    /// <summary>
    /// No lifecycle constraint — every item regardless of status. The default when
    /// <c>filterBy</c> is omitted: absence of the param must not add a hidden constraint,
    /// and seller/winner profile queries need finished items too (e.g. a seller's sold
    /// history), so "all" — not "live" — is the correct default.
    /// </summary>
    All,

    /// <summary>Status is <c>"Live"</c> and AuctionEnd is still in the future.</summary>
    Live,

    /// <summary>
    /// Status is <c>"Live"</c>, AuctionEnd is in the future, and AuctionEnd falls within
    /// <see cref="Models.ItemSearchDefaults.EndingSoonWindow"/> of now.
    /// </summary>
    EndingSoon,

    /// <summary>
    /// AuctionEnd has passed, OR Status is anything other than <c>"Live"</c>. The OR covers
    /// Finished, ReserveNotMet, a future Cancelled, and the case where the auction has ended
    /// but the finalizing event hasn't landed in this index yet.
    /// </summary>
    Finished
}
