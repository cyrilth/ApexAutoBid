namespace BiddingService.Application.DTOs;

/// <summary>
/// Read DTO returned by both <c>POST api/bids</c> and <c>GET api/bids/{auctionId}</c>.
/// <para>
/// <b>Privacy:</b> <c>BidderEmail</c> is intentionally absent — Requirements §3.3 is explicit
/// that it is captured for the (later) post-sale <c>AuctionFinished.WinnerEmail</c> flow only
/// and must never be returned by any bids API response.
/// </para>
/// <para>
/// <b>BidStatus</b> is a <c>string</c> rather than the Domain <c>BidStatus</c> enum so API
/// consumers stay decoupled from the server-side enum definition — mirrors the event-contract
/// convention (<c>Contracts.BidPlaced.BidStatus</c>) and <c>AuctionService</c>'s identical
/// choice for <c>AuctionDto.Status</c>.
/// </para>
/// </summary>
public class BidDto
{
    public Guid Id { get; init; }
    public Guid AuctionId { get; init; }
    public required string Bidder { get; init; }
    public DateTime BidTime { get; init; }
    public int Amount { get; init; }
    public required string BidStatus { get; init; }
}
