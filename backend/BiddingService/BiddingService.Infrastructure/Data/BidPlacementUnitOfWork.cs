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
/// <see cref="IBidPlacementUnitOfWork"/> implementation using MassTransit's MongoDB
/// transactional "bus outbox" (<c>AddMongoDbOutbox</c> + <c>UseBusOutbox()</c>, wired in
/// <c>Program.cs</c>) so a newly placed bid's document write and its <c>BidPlaced</c> publish
/// commit — or roll back — together.
/// <para>
/// <b>Mechanics, live-verified against the running dev MongoDB (single-node replica set) and
/// RabbitMQ during this task — not assumed from documentation:</b> the scoped
/// <see cref="MongoDbContext"/> MassTransit registers when <c>UseBusOutbox()</c> is configured
/// exposes <c>StartSession</c>/<c>BeginTransaction</c>/<c>CommitTransaction</c>/
/// <c>AbortTransaction</c> plus <c>GetCollection&lt;T&gt;()</c> — a transaction-enlisted
/// collection handle, distinct from <see cref="MongoDbConnection"/>'s MongoDB.Entities-backed
/// read path (<see cref="BidRepository"/>). Writing the bid through that collection handle,
/// then calling <see cref="IPublishEndpoint.Publish{T}(T, CancellationToken)"/> (resolved from
/// the <b>same</b> DI scope, so MassTransit's own scoped bus context enlists the send in the
/// same transaction) and finally committing was confirmed end-to-end in a standalone harness
/// against this repo's actual dev infra to: (a) make the write visible only after
/// <c>CommitTransaction</c> — never before, (b) roll back the write cleanly on
/// <c>AbortTransaction</c> with nothing left behind, and (c) actually deliver the message
/// through RabbitMQ to a real consumer once committed (not merely "queued" in the outbox
/// table). All three were exercised, not merely reasoned about.
/// </para>
/// <para>
/// <b>Registration requirement:</b> <c>MongoDbContext.GetCollection&lt;BidDocument&gt;()</c>
/// internally resolves <c>IMongoCollection&lt;BidDocument&gt;</c> from the DI container — this
/// must be registered explicitly (<c>InfrastructureServiceExtensions</c>) or the very first
/// call throws <c>InvalidOperationException</c>. This is specific to
/// MassTransit.MongoDbIntegration's bus-outbox collection resolution and is unrelated to (and
/// does not replace) the <c>IMongoDatabase</c>/<c>IMongoClient</c> registrations that outbox
/// message storage itself needs. <c>IMongoCollection&lt;AuctionDocument&gt;</c> is registered
/// there too (originally for <c>AuctionFinalizationUnitOfWork</c>) — reused below for the
/// atomic-accept claim.
/// </para>
/// <para>
/// <b>Atomic accept (phase-end code review Critical 1):</b> the previous design read the
/// auction's current high bid via <c>IBidRepository.GetHighestAcceptedAmountAsync</c> BEFORE
/// this transaction even started — two concurrent bids could both read the same stale high and
/// both get recorded/published as Accepted or AcceptedBelowReserve, even though only one of
/// them should have. That earlier read still happens (in <c>BidAppService.DetermineStatusAsync</c>)
/// but is now merely a non-authoritative pre-check: whenever it produces a tentative
/// <see cref="BidStatus.Accepted"/>/<see cref="BidStatus.AcceptedBelowReserve"/>, THIS class
/// re-verifies it atomically, inside the same transaction as the bid insert and the
/// <c>BidPlaced</c> publish, via a conditional claim against <see cref="AuctionDocument.CurrentHigh"/>
/// (<c>MongoDbCollectionContext&lt;T&gt;.Lock</c> — decompile-confirmed during this task to be
/// MassTransit.MongoDbIntegration's own session-enlisted <c>FindOneAndUpdate</c>, returning the
/// post-update document when its filter matches or <see langword="null"/> when it doesn't). A
/// failed claim means some other bid's claim already raised <c>CurrentHigh</c> to at least this
/// amount — <see cref="AttemptSaveAsync"/> downgrades the bid to <see cref="BidStatus.TooLow"/>
/// IN PLACE rather than re-running <c>DetermineStatusAsync</c>'s whole computation, because the
/// claim's own "<c>CurrentHigh &lt; Amount</c>" condition IS, verbatim, that method's
/// "<c>amount &lt;= currentHighBid -> TooLow</c>" rule — just evaluated atomically instead of
/// against the stale pre-check read. No <c>BidPlaced</c> is published for a downgraded bid,
/// mirroring every other TooLow bid.
/// </para>
/// <para>
/// <b>Bounded retry on a transient write conflict (Critical 1):</b> two bids racing to claim
/// the SAME <see cref="AuctionDocument"/> can hit a Mongo "WriteConflict"/"TransientTransactionError"
/// — the driver's own signal that the WHOLE transaction must be retried from the start, not
/// merely re-attempted at the point of failure. <c>Lock</c> already retries (unboundedly,
/// internally, by aborting and restarting the same transaction) a conflict it hits doing that
/// specific update; <see cref="SaveAsync"/>'s own outer, small, BOUNDED retry (logged at
/// Warning) is a deliberate backstop for any other transient conflict surfacing elsewhere in
/// the same attempt (the bid insert, the publish, or the commit itself) — not a duplicate of
/// <c>Lock</c>'s internal handling.
/// </para>
/// </summary>
public class BidPlacementUnitOfWork(
    MongoDbContext mongoDbContext,
    IPublishEndpoint publishEndpoint,
    ILogger<BidPlacementUnitOfWork> logger) : IBidPlacementUnitOfWork
{
    /// <summary>
    /// Small and bounded on purpose: this retries a whole place-bid attempt synchronously
    /// within a single HTTP request, so it must stay well inside that request's own latency
    /// budget while still comfortably absorbing the kind of fleeting same-document contention
    /// concurrent bidding on one auction produces.
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task SaveAsync(Bid bid, BidPlaced? bidPlacedEvent, CancellationToken cancellationToken)
    {
        // Captured once, up front: a retried attempt must always re-evaluate the atomic claim
        // from this SAME tentative status, never from whatever a PRIOR, fully-aborted attempt
        // may have downgraded bid.BidStatus to in place — that attempt's transaction (and
        // therefore its downgrade decision) no longer exists once it's been rolled back.
        var tentativeStatus = bid.BidStatus;

        for (var attempt = 1; ; attempt++)
        {
            bid.BidStatus = tentativeStatus;
            var attemptEvent = bidPlacedEvent;

            try
            {
                await AttemptSaveAsync(bid, attemptEvent, cancellationToken);
                return;
            }
            catch (MongoException ex) when (attempt < MaxAttempts && ex.HasErrorLabel("TransientTransactionError"))
            {
                logger.LogWarning(ex,
                    "Bid placement for auction {AuctionId} hit a transient Mongo write conflict on attempt {Attempt}/{MaxAttempts} — retrying the whole attempt",
                    bid.AuctionId, attempt, MaxAttempts);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Bid placement transaction failed for auction {AuctionId} — aborting", bid.AuctionId);
                throw;
            }
        }
    }

    private async Task AttemptSaveAsync(Bid bid, BidPlaced? bidPlacedEvent, CancellationToken cancellationToken)
    {
        await mongoDbContext.StartSession(cancellationToken);
        await mongoDbContext.BeginTransaction(cancellationToken);

        try
        {
            if (bid.BidStatus is BidStatus.Accepted or BidStatus.AcceptedBelowReserve)
            {
                // Atomic claim: only succeeds while CurrentHigh is still strictly less than
                // this bid's Amount — exactly BidAppService.DetermineStatusAsync's own
                // "amount <= currentHighBid -> TooLow" rule, evaluated against the live,
                // transaction-consistent value rather than the earlier pre-check read.
                var filter = Builders<AuctionDocument>.Filter.And(
                    Builders<AuctionDocument>.Filter.Eq(d => d.Id, bid.AuctionId),
                    Builders<AuctionDocument>.Filter.Lt(d => d.CurrentHigh, bid.Amount));
                var update = Builders<AuctionDocument>.Update.Set(d => d.CurrentHigh, bid.Amount);

                var claimed = await mongoDbContext.GetCollection<AuctionDocument>()
                    .Lock(filter, update, cancellationToken);

                if (claimed is null)
                {
                    // Someone else's bid already reached (or exceeded) this amount — downgrade
                    // in place (see this class's own remarks for why this is not an
                    // approximation) rather than re-running the whole status computation. No
                    // BidPlaced is published for it.
                    bid.BidStatus = BidStatus.TooLow;
                    bidPlacedEvent = null;
                }
            }

            var document = BidDocumentMapper.ToDocument(bid);
            await mongoDbContext.GetCollection<BidDocument>().InsertOne(document, cancellationToken);

            if (bidPlacedEvent is not null)
                await publishEndpoint.Publish(bidPlacedEvent, cancellationToken);

            await mongoDbContext.CommitTransaction(cancellationToken);
        }
        catch (Exception)
        {
            await mongoDbContext.AbortTransaction(cancellationToken);
            throw;
        }
    }
}
