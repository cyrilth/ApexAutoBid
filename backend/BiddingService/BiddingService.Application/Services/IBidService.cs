using BiddingService.Application.DTOs;

namespace BiddingService.Application.Services;

/// <summary>
/// Result codes for <see cref="IBidService.PlaceBidAsync"/> so the controller can map
/// outcomes to HTTP status codes without the Application layer having any knowledge of HTTP.
/// Mirrors the <c>(outcome enum, DTO)</c> pattern used by
/// <c>AuctionService.Application.Services.IAuctionService</c>/
/// <c>SearchService.Application.Services.ISearchService</c>.
/// </summary>
public enum BidOutcome
{
    /// <summary>
    /// The bid was recorded. <see cref="Domain.Enums.BidStatus.TooLow"/> and
    /// <see cref="Domain.Enums.BidStatus.Finished"/> are included here — Requirements §3.3 /
    /// Task 21: these are normal outcomes carried in <c>BidDto.BidStatus</c>, never HTTP
    /// errors.
    /// </summary>
    Placed,

    /// <summary>
    /// The auction could not be resolved locally (today) or via the gRPC fallback (later) —
    /// 404 per Requirements §3.3.
    /// </summary>
    AuctionNotFound,

    /// <summary>The bidder is the auction's own seller — 400 per Requirements §3.3.</summary>
    SellerCannotBid
}

/// <summary>
/// Application-level service for bid placement and retrieval (Requirements §3.3). Controllers
/// depend only on this interface — never on <c>IBidRepository</c>, <c>IAuctionProvider</c>, or
/// any Infrastructure type.
/// </summary>
public interface IBidService
{
    /// <summary>
    /// Validates and records a bid from <paramref name="bidder"/>/<paramref name="bidderEmail"/>
    /// (the caller's "username"/"email" claims) against <paramref name="dto"/>. See
    /// <see cref="BidOutcome"/> for how outcomes map to HTTP status codes.
    /// </summary>
    Task<(BidOutcome Outcome, BidDto? Bid)> PlaceBidAsync(
        PlaceBidDto dto, string bidder, string bidderEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every bid for the given auction (Anon — <c>GET api/bids/{auctionId}</c>),
    /// newest first. Never includes <c>BidderEmail</c>.
    /// </summary>
    Task<List<BidDto>> GetBidsForAuctionAsync(Guid auctionId, CancellationToken cancellationToken);
}
