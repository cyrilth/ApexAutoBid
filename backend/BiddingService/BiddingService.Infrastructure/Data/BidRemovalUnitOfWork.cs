using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using Contracts;
using MassTransit;
using MassTransit.MongoDbIntegration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// <see cref="IBidRemovalUnitOfWork"/> implementation — reuses the exact same MassTransit
/// MongoDB transactional "bus outbox" mechanics <see cref="BidPlacementUnitOfWork"/>/
/// <see cref="AuctionFinalizationUnitOfWork"/> use (Phase 11 Task 5.1/5.5), applied here to
/// admin bid removal: the bid deletion, the recalculated <see cref="AuctionDocument.CurrentHigh"/>,
/// the <c>BidRemoved</c> publish, and the <see cref="AuditEntryDocument"/> insert all commit — or
/// roll back — together.
/// </summary>
/// <remarks>
/// <b>Recalculation happens in C#, not via a Mongo-side sort/limit:</b> the remaining
/// <c>Accepted</c>/<c>AcceptedBelowReserve</c> bids for the auction (after this deletion) are
/// pulled into memory via <c>MongoDbCollectionContext&lt;T&gt;.Find</c>'s session-enlisted cursor,
/// then reduced with the exact same deterministic tiebreak <c>IBidRepository.GetHighestAcceptedAmountAsync</c>
/// documents (Amount desc, BidTime asc, Id asc) — a per-auction bid count is small enough that
/// this is simpler (and, unlike a Mongo-side <c>Sort</c>/<c>Limit</c> chain layered on the
/// session-enlisted <c>Find</c>, trivially unit-testable against a substituted cursor) without
/// any real behavioural difference.
/// <para>
/// <b>No bounded retry loop (unlike <see cref="BidPlacementUnitOfWork"/>):</b> admin bid removal
/// is a one-off, low-frequency operation with no legitimate concurrent writer racing to claim
/// the SAME auction document the way concurrent bidders do — a single attempt, abort-and-propagate-
/// on-failure, mirrors <see cref="AuctionFinalizationUnitOfWork"/>'s identical (non-retrying)
/// convention. The one write here that (as an implementation detail) issues a "lock"-shaped
/// update still uses the unconditional single-document filter (<c>Eq(Id)</c> only, no compare
/// condition), so <c>MongoDbCollectionContext&lt;T&gt;.Lock</c>'s own internal conflict handling is
/// never expected to retry more than transiently.
/// </para>
/// </remarks>
public class BidRemovalUnitOfWork(
    MongoDbContext mongoDbContext,
    IPublishEndpoint publishEndpoint,
    ILogger<BidRemovalUnitOfWork> logger) : IBidRemovalUnitOfWork
{
    public async Task<int?> RemoveAsync(Bid bid, AuditEntry auditEntry, CancellationToken cancellationToken)
    {
        await mongoDbContext.StartSession(cancellationToken);
        await mongoDbContext.BeginTransaction(cancellationToken);

        try
        {
            var deleteFilter = Builders<BidDocument>.Filter.Eq(d => d.Id, bid.Id);
            await mongoDbContext.GetCollection<BidDocument>().DeleteOne(deleteFilter, cancellationToken);

            var newCurrentHigh = await RecalculateCurrentHighAsync(bid.AuctionId, cancellationToken);

            // Unconditional — no compare-and-swap needed (see this class's own remarks): unlike
            // the atomic-accept claim in BidPlacementUnitOfWork, there is no concurrent writer
            // racing to set CurrentHigh to a DIFFERENT value at the same time as this admin
            // operation; the recalculation above is already this transaction's own authoritative
            // answer.
            var auctionFilter = Builders<AuctionDocument>.Filter.Eq(d => d.Id, bid.AuctionId);
            var auctionUpdate = Builders<AuctionDocument>.Update.Set(d => d.CurrentHigh, newCurrentHigh ?? 0);
            await mongoDbContext.GetCollection<AuctionDocument>().Lock(auctionFilter, auctionUpdate, cancellationToken);

            var bidRemovedEvent = new BidRemoved(bid.Id.ToString(), bid.AuctionId.ToString(), newCurrentHigh);
            await publishEndpoint.Publish(bidRemovedEvent, cancellationToken);

            var auditDocument = new AuditEntryDocument
            {
                Id = auditEntry.Id,
                Timestamp = auditEntry.Timestamp,
                Actor = auditEntry.Actor,
                ActorIsAdmin = auditEntry.ActorIsAdmin,
                Action = auditEntry.Action,
                EntityType = auditEntry.EntityType,
                EntityId = auditEntry.EntityId,
                Data = auditEntry.Data
            };
            await mongoDbContext.GetCollection<AuditEntryDocument>().InsertOne(auditDocument, cancellationToken);

            await mongoDbContext.CommitTransaction(cancellationToken);
            return newCurrentHigh;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bid removal transaction failed for bid {BidId} — aborting", bid.Id);
            await mongoDbContext.AbortTransaction(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Returns the highest <c>Amount</c> among the auction's remaining <c>Accepted</c>/
    /// <c>AcceptedBelowReserve</c> bids (the just-deleted bid already excluded, since this reads
    /// through the SAME session/transaction the delete above ran in), or <see langword="null"/>
    /// when none remain — same tiebreak as <c>IBidRepository.GetHighestAcceptedAmountAsync</c>
    /// (Amount desc, BidTime asc, Id asc).
    /// </summary>
    private async Task<int?> RecalculateCurrentHighAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        var filter = Builders<BidDocument>.Filter.And(
            Builders<BidDocument>.Filter.Eq(d => d.AuctionId, auctionId),
            Builders<BidDocument>.Filter.Or(
                Builders<BidDocument>.Filter.Eq(d => d.BidStatus, nameof(BidStatus.Accepted)),
                Builders<BidDocument>.Filter.Eq(d => d.BidStatus, nameof(BidStatus.AcceptedBelowReserve))));

        var remaining = new List<BidDocument>();

        using var cursor = await mongoDbContext.GetCollection<BidDocument>().Find(filter).ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            remaining.AddRange(cursor.Current);
        }

        return remaining
            .OrderByDescending(d => d.Amount)
            .ThenBy(d => d.BidTime)
            .ThenBy(d => d.Id)
            .Select(d => (int?)d.Amount)
            .FirstOrDefault();
    }
}
