using System.Text.Json;
using BiddingService.Application.DTOs;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BiddingService.Application.Services;

/// <summary>
/// <see cref="IAdminBidService"/> implementation — the admin bid-moderation logic (Phase 11
/// Task 5.1/5.4/5.5) lives entirely here so it stays unit-testable independent of MongoDB or
/// MassTransit. Every action here is admin-only (enforced by <c>[Authorize(Roles = "admin")]</c>
/// on <c>AdminBidsController</c>, Requirements §10) — <see cref="AuditEntry.ActorIsAdmin"/> is
/// therefore always <see langword="true"/>, unlike <c>AuctionAppService</c>'s equivalent (whose
/// mutating endpoints are reachable by ordinary sellers too).
/// </summary>
public class AdminBidAppService(
    IBidRepository bidRepository,
    IBidRemovalUnitOfWork removalUnitOfWork,
    TimeProvider timeProvider,
    ILogger<AdminBidAppService> logger) : IAdminBidService
{
    public async Task<RemoveBidOutcome> RemoveBidAsync(Guid bidId, string actor, CancellationToken cancellationToken)
    {
        var bid = await bidRepository.GetByIdAsync(bidId, cancellationToken);
        if (bid is null)
        {
            logger.LogWarning("Admin {Actor} attempted to remove bid {BidId} — not found", actor, bidId);
            return RemoveBidOutcome.NotFound;
        }

        // Append-only audit record (Requirements §13.3) — captures the removed bid's own
        // details (bidder, amount, time, auction), never BidderEmail (never logged/exposed
        // outside the post-sale contact exchange — Requirements §13.5). Constructed BEFORE the
        // unit of work runs so it commits in the SAME Mongo operation scope as the bid deletion,
        // the CurrentHigh recalculation, and the BidRemoved publish — "best effort" per
        // Requirements §13.3's MongoDB carve-out.
        var auditEntry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = timeProvider.GetUtcNow().UtcDateTime,
            Actor = actor,
            ActorIsAdmin = true,
            Action = "BidRemoved",
            EntityType = "Bid",
            EntityId = bid.Id.ToString(),
            Data = JsonSerializer.Serialize(new
            {
                bid.Id,
                bid.AuctionId,
                bid.Bidder,
                bid.Amount,
                bid.BidTime,
                BidStatus = bid.BidStatus.ToString()
            })
        };

        var currentHighBid = await removalUnitOfWork.RemoveAsync(bid, auditEntry, cancellationToken);

        // Bidder (username) is fine to log; BidderEmail never is (Requirements §13.5) — mirrors
        // BidAppService.PlaceBidAsync's identical logging convention.
        logger.LogInformation(
            "Admin {Actor} removed bid {BidId} placed by {Bidder} on auction {AuctionId} — recalculated current high bid {CurrentHighBid}",
            actor, bid.Id, bid.Bidder, bid.AuctionId, currentHighBid?.ToString() ?? "(none)");

        return RemoveBidOutcome.Removed;
    }

    public async Task<BidStatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        var total = await bidRepository.CountAsync(cancellationToken);
        return new BidStatsDto { TotalBids = total };
    }
}
