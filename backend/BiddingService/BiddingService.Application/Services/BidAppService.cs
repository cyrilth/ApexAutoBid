using BiddingService.Application.DTOs;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using Contracts;
using MapsterMapper;
using Microsoft.Extensions.Logging;

namespace BiddingService.Application.Services;

/// <summary>
/// <see cref="IBidService"/> implementation — the bid validation/status logic (Requirements
/// §3.3 / Task 10) lives entirely here so it stays unit-testable independent of MongoDB,
/// MassTransit, or ASP.NET Core.
/// </summary>
public class BidAppService(
    IBidRepository bidRepository,
    IAuctionProvider auctionProvider,
    IBidPlacementUnitOfWork placementUnitOfWork,
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<BidAppService> logger) : IBidService
{
    public async Task<(BidOutcome Outcome, BidDto? Bid)> PlaceBidAsync(
        PlaceBidDto dto, string bidder, string bidderEmail, CancellationToken cancellationToken)
    {
        // The auction "must exist locally or be fetchable via the gRPC fallback — otherwise
        // 404" (Requirements §3.3). IAuctionProvider is the seam: today this only ever
        // resolves locally (LocalAuctionProvider); the later gRPC-fallback run replaces the
        // DI registration, not this call site — see IAuctionProvider's remarks.
        var auction = await auctionProvider.GetAuctionAsync(dto.AuctionId, cancellationToken);
        if (auction is null)
        {
            logger.LogWarning("PlaceBid: auction {AuctionId} not found", dto.AuctionId);
            return (BidOutcome.AuctionNotFound, null);
        }

        // The seller cannot bid on their own auction (Requirements §3.3) — 400 Bad Request.
        if (auction.Seller == bidder)
        {
            logger.LogWarning(
                "PlaceBid: seller {Seller} attempted to bid on their own auction {AuctionId}",
                bidder, auction.Id);
            return (BidOutcome.SellerCannotBid, null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var status = await DetermineStatusAsync(auction, dto.Amount, now, cancellationToken);

        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auction.Id,
            Bidder = bidder,
            BidderEmail = bidderEmail,
            BidTime = now,
            Amount = dto.Amount,
            BidStatus = status
        };

        // Only Accepted/AcceptedBelowReserve represent a genuine new high bid worth
        // broadcasting to the bus — mirrors AuctionService's/SearchService's BidPlacedConsumer
        // semantics exactly (both already special-case exactly these two statuses). TooLow
        // and Finished bids are still recorded (Requirements §3.3) but no BidPlaced is
        // published for them.
        BidPlaced? bidPlacedEvent = status is BidStatus.Accepted or BidStatus.AcceptedBelowReserve
            ? mapper.Map<BidPlaced>(bid)
            : null;

        await placementUnitOfWork.SaveAsync(bid, bidPlacedEvent, cancellationToken);

        // bid.BidStatus is read AFTER SaveAsync returns — not the `status` local computed
        // above — because the unit of work may have downgraded it in place to TooLow if this
        // bid lost the atomic race to claim the auction's current high (phase-end code review
        // Critical 1; see IBidPlacementUnitOfWork's remarks). This log line, and the DTO mapped
        // below, must reflect that final, authoritative outcome, not the necessarily-stale
        // pre-check computed above. Bidder (username) is fine to log; BidderEmail never is
        // (Requirements §13.5).
        logger.LogInformation(
            "Bid placed on auction {AuctionId} by {Bidder} for {Amount} — outcome {BidStatus}",
            auction.Id, bidder, dto.Amount, bid.BidStatus);

        return (BidOutcome.Placed, mapper.Map<BidDto>(bid));
    }

    public async Task<List<BidDto>> GetBidsForAuctionAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        var bids = await bidRepository.GetByAuctionIdAsync(auctionId, cancellationToken);
        return mapper.Map<List<BidDto>>(bids);
    }

    /// <summary>
    /// Implements Requirements §3.3's bid-status rules in order: an ended auction always
    /// yields <see cref="BidStatus.Finished"/>, regardless of amount; otherwise the amount is
    /// compared against the current high bid (derived from this service's own bid history —
    /// see <see cref="Auction"/>'s remarks) and, only when it beats that, against the reserve
    /// price. The reserve comparison is <c>&gt;=</c> (not <c>&gt;</c>) — Requirements §3.3's
    /// literal wording ("... and ≥ reserve price → Accepted; ... but below reserve →
    /// AcceptedBelowReserve").
    /// </summary>
    /// <remarks>
    /// <b>This "current high bid" read is a pre-check only, not authoritative</b> (phase-end
    /// code review Critical 1): it runs BEFORE <c>IBidPlacementUnitOfWork.SaveAsync</c>'s own
    /// transaction starts, so it can be stale under concurrent bidding on the same auction —
    /// two bids racing this exact read could otherwise both see the same value and both be
    /// (wrongly) deemed Accepted/AcceptedBelowReserve. The tentative
    /// Accepted/AcceptedBelowReserve status this method returns is re-verified atomically
    /// inside that later transaction, against <c>AuctionDocument.CurrentHigh</c> — the
    /// Infrastructure-only field that's actually race-proof — and may be downgraded to
    /// <see cref="BidStatus.TooLow"/> there; see <c>IBidPlacementUnitOfWork</c>'s remarks.
    /// </remarks>
    private async Task<BidStatus> DetermineStatusAsync(
        Auction auction, int amount, DateTime now, CancellationToken cancellationToken)
    {
        if (auction.Finished || now >= auction.AuctionEnd)
            return BidStatus.Finished;

        var currentHighBid = await bidRepository.GetHighestAcceptedAmountAsync(auction.Id, cancellationToken) ?? 0;

        if (amount <= currentHighBid)
            return BidStatus.TooLow;

        return amount >= auction.ReservePrice ? BidStatus.Accepted : BidStatus.AcceptedBelowReserve;
    }
}
