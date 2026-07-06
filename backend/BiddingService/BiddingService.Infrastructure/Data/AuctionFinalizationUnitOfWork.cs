using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using Contracts;
using MassTransit;
using MassTransit.MongoDbIntegration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// <see cref="IAuctionFinalizationUnitOfWork"/> implementation — reuses the exact same
/// MassTransit MongoDB transactional "bus outbox" mechanics <see cref="BidPlacementUnitOfWork"/>
/// uses for bid placement (see that class's remarks for the live-verified transaction
/// semantics: write visibility only after commit, clean rollback on abort, and real RabbitMQ
/// delivery once committed), applied here to the background finalizer (Phase 5 Tasks 11/12).
/// </summary>
/// <remarks>
/// <b>Atomic finalize (phase-end code review Critical 2):</b> previously this replaced the
/// WHOLE <see cref="AuctionDocument"/> (<c>Adapt&lt;AuctionDocument&gt;()</c> from the in-memory
/// <see cref="Auction"/>, via <c>FindOneAndReplace</c>) unconditionally by Id. That had two
/// problems: (1) nothing prevented two concurrent finalization passes for the SAME auction
/// (one process racing another, or a retried tick) from both "winning" and both publishing
/// <see cref="AuctionFinished"/>; (2) the in-memory <see cref="Auction"/> projection never
/// carries <see cref="AuctionDocument.CurrentHigh"/> (Critical 1 — see that field's own
/// remarks), so a whole-document replace silently reset it back to its default on every single
/// finalize, discarding it for no reason. Both are fixed the same way: a conditional
/// <c>FindOneAndUpdate</c> (<c>MongoDbCollectionContext&lt;T&gt;.Lock</c> — decompile-confirmed
/// during this task to be MassTransit.MongoDbIntegration's own session-enlisted
/// <c>FindOneAndUpdate</c>, returning the post-update document when its filter matches or
/// <see langword="null"/> when it doesn't) that (a) matches ONLY while <c>Finished</c> is still
/// <see langword="false"/> — so double-finalization (and a duplicate publish) is structurally
/// impossible, not merely unlikely, even across genuinely concurrent processes — and (b) sets
/// ONLY <c>Finished = true</c>, touching no other field, so <c>CurrentHigh</c> (and every other
/// field) is left exactly as it was.
/// </remarks>
public class AuctionFinalizationUnitOfWork(
    MongoDbContext mongoDbContext,
    IPublishEndpoint publishEndpoint,
    ILogger<AuctionFinalizationUnitOfWork> logger) : IAuctionFinalizationUnitOfWork
{
    public async Task<bool> FinalizeAsync(Auction auction, AuctionFinished finishedEvent, CancellationToken cancellationToken)
    {
        await mongoDbContext.StartSession(cancellationToken);
        await mongoDbContext.BeginTransaction(cancellationToken);

        try
        {
            var filter = Builders<AuctionDocument>.Filter.And(
                Builders<AuctionDocument>.Filter.Eq(d => d.Id, auction.Id),
                Builders<AuctionDocument>.Filter.Eq(d => d.Finished, false));
            var update = Builders<AuctionDocument>.Update.Set(d => d.Finished, true);

            var claimed = await mongoDbContext.GetCollection<AuctionDocument>()
                .Lock(filter, update, cancellationToken);

            if (claimed is null)
            {
                // Already finalized by a concurrent pass — a normal, idempotent no-op, not a
                // failure: whichever pass's claim committed first already published (or is
                // publishing) the authoritative AuctionFinished. Nothing to publish here.
                await mongoDbContext.CommitTransaction(cancellationToken);
                return false;
            }

            await publishEndpoint.Publish(finishedEvent, cancellationToken);

            await mongoDbContext.CommitTransaction(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Auction finalization transaction failed for auction {AuctionId} — aborting", auction.Id);
            await mongoDbContext.AbortTransaction(cancellationToken);
            throw;
        }
    }
}
