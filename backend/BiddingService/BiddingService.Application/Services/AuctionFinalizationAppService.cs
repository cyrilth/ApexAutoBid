using BiddingService.Application.Configuration;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingService.Application.Services;

/// <summary>
/// <see cref="IAuctionFinalizationService"/> implementation — the auction-finalization logic
/// (Requirements §3.3 / Task 11–12) lives entirely here so it stays unit-testable independent
/// of MongoDB, MassTransit, or the hosted-service timer that drives it.
/// </summary>
public class AuctionFinalizationAppService(
    IAuctionRepository auctionRepository,
    IBidRepository bidRepository,
    IAuctionFinalizationUnitOfWork unitOfWork,
    IFinalizationFailureTracker failureTracker,
    IOptions<FinalizationOptions> finalizationOptions,
    TimeProvider timeProvider,
    ILogger<AuctionFinalizationAppService> logger) : IAuctionFinalizationService
{
    /// <summary>
    /// Consecutive per-auction finalization failures before Warning 4's escalation from
    /// <c>LogWarning</c> to <c>LogError</c> — 3 ticks (~30s at the default 10s finalizer
    /// interval) is long enough to absorb one isolated transient Mongo/RabbitMQ blip quietly,
    /// while still surfacing a genuinely stuck auction well before it goes unnoticed.
    /// </summary>
    private const int StuckAuctionFailureThreshold = 3;

    public async Task FinalizeExpiredAuctionsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Grace period (phase-end code review Critical 2): eligibility is
        // "AuctionEnd + FinalizationGraceSeconds <= now", computed here as
        // "AuctionEnd <= now - FinalizationGraceSeconds" so
        // IAuctionRepository.GetExpiredUnfinalizedAsync's own filter shape never has to change —
        // see FinalizationOptions' remarks for the full rationale.
        var cutoff = now.AddSeconds(-finalizationOptions.Value.FinalizationGraceSeconds);

        var expired = await auctionRepository.GetExpiredUnfinalizedAsync(cutoff, cancellationToken);
        if (expired.Count == 0)
            return;

        logger.LogInformation(
            "Auction finalizer found {Count} expired auction(s) to finalize", expired.Count);

        // Each auction is finalized independently — one failing (a transient Mongo/RabbitMQ
        // blip) must not stop the rest of this tick's batch from finalizing. The failed one
        // simply stays !Finished and is picked up again by GetExpiredUnfinalizedAsync on the
        // next tick (Task 12's "survive transient errors" requirement).
        foreach (var auction in expired)
        {
            try
            {
                await FinalizeOneAsync(auction, cancellationToken);
                failureTracker.RecordSuccess(auction.Id);
            }
            catch (Exception ex)
            {
                // Warning 4 — stuck-auction visibility: an isolated failure logs at Warning
                // (already includes the auction id); once the SAME auction has failed on
                // StuckAuctionFailureThreshold consecutive ticks, escalate to Error so a
                // genuinely stuck auction (failing every tick, not just once) is unmissable in
                // normal log monitoring, without any metrics infrastructure.
                var consecutiveFailures = failureTracker.RecordFailure(auction.Id);
                if (consecutiveFailures >= StuckAuctionFailureThreshold)
                {
                    logger.LogError(ex,
                        "Auction {AuctionId} has failed to finalize on {ConsecutiveFailures} consecutive tick(s) — appears stuck and needs investigation",
                        auction.Id, consecutiveFailures);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Failed to finalize auction {AuctionId} (consecutive failure {ConsecutiveFailures}/{Threshold}) — will retry next tick",
                        auction.Id, consecutiveFailures, StuckAuctionFailureThreshold);
                }
            }
        }
    }

    private async Task FinalizeOneAsync(Auction auction, CancellationToken cancellationToken)
    {
        // Only a strictly Accepted bid represents a genuine sale (Requirements §3.3/§8.3) — a
        // highest bid that is merely AcceptedBelowReserve means the item did NOT sell. See
        // IBidRepository.GetHighestAcceptedBidAsync's own remarks for why this is deliberately
        // NOT the same "current high bid" notion BidAppService uses while an auction is live.
        var winningBid = await bidRepository.GetHighestAcceptedBidAsync(auction.Id, cancellationToken);

        var finishedEvent = new AuctionFinished(
            ItemSold: winningBid is not null,
            AuctionId: auction.Id.ToString(),
            Winner: winningBid?.Bidder,
            // WinnerEmail is set from the winning bid's BidderEmail ONLY when the item actually
            // sold — this event is the one sanctioned carrier for the post-sale contact
            // exchange (Requirements §3.1/§3.3); never populated, logged, or otherwise
            // referenced for an unsold auction.
            WinnerEmail: winningBid?.BidderEmail,
            Seller: auction.Seller,
            Amount: winningBid?.Amount);

        auction.Finished = true;

        // Atomic AND conditional (phase-end code review Critical 2): the Finished=true write
        // and the AuctionFinished publish commit together, and ONLY if this auction isn't
        // already Finished — see IAuctionFinalizationUnitOfWork's remarks. A false return means
        // a concurrent pass already won this race; that is a normal, idempotent no-op here, not
        // a failure (so it must NOT throw, and NOT count towards Warning 4's failure streak).
        var finalized = await unitOfWork.FinalizeAsync(auction, finishedEvent, cancellationToken);
        if (!finalized)
        {
            logger.LogInformation(
                "Auction {AuctionId} was already finalized by a concurrent finalization pass — skipping",
                auction.Id);
            return;
        }

        // Bidder (username) is fine to log; BidderEmail never is (Requirements §13.5) — mirrors
        // BidAppService.PlaceBidAsync's identical logging convention.
        logger.LogInformation(
            "Auction {AuctionId} finalized — sold={ItemSold}, winner={Winner}",
            auction.Id, finishedEvent.ItemSold, winningBid?.Bidder ?? "(none)");
    }
}
