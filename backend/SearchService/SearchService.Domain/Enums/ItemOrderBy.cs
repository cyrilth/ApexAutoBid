namespace SearchService.Domain.Enums;

/// <summary>
/// Sort options for <c>GET api/search</c> (Requirements §3.2 <c>orderBy</c> param).
/// </summary>
public enum ItemOrderBy
{
    /// <summary>AuctionEnd ascending — items closing soonest first. The default.</summary>
    EndingSoon,

    /// <summary>Make ascending, then Model ascending.</summary>
    Make,

    /// <summary>CreatedAt descending — most recently indexed items first.</summary>
    New
}
