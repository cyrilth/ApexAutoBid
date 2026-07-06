using BiddingService.Domain.Enums;

namespace BiddingService.Domain.Entities;

/// <summary>
/// A single bid placed on an auction (Requirements §3.3). Every bid is persisted regardless
/// of outcome — <see cref="BidStatus.TooLow"/> and <see cref="BidStatus.Finished"/> bids are
/// recorded for history but never raise the auction's current high bid (see
/// <c>IBidRepository.GetHighestAcceptedAmountAsync</c>).
/// </summary>
public class Bid
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }

    /// <summary>Captured from the "username" claim at bid time.</summary>
    public required string Bidder { get; set; }

    /// <summary>
    /// Captured from the "email" claim at bid time. Stored so the (later) background
    /// finalizer can set <c>AuctionFinished.WinnerEmail</c> from the winning bid (post-sale
    /// contact exchange — Requirements §3.1/§3.3). <b>Never</b> returned by
    /// <c>GET api/bids/{auctionId}</c> and never logged (Requirements §13.5).
    /// </summary>
    public required string BidderEmail { get; set; }

    public DateTime BidTime { get; set; } = DateTime.UtcNow;
    public int Amount { get; set; }
    public BidStatus BidStatus { get; set; }
}
