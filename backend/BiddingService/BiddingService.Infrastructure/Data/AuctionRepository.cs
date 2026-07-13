using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using Mapster;
using MongoDB.Driver;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// <see cref="IAuctionRepository"/> implementation backed by MongoDB.Entities against
/// <see cref="AuctionDocument"/> for this service's own local Auction projection. Uses
/// Mapster's ambient <c>.Adapt&lt;T&gt;()</c> extension directly — safe here specifically
/// because every field <c>Auction</c> itself has matches, by name and type, on
/// <c>AuctionDocument</c>, with no enum/string mismatch (unlike <see cref="BidRepository"/>'s <c>Bid</c>/
/// <c>BidDocument</c> pair — see <see cref="BidDocumentMapper"/>'s remarks); the one field
/// <c>AuctionDocument</c> alone carries (<see cref="AuctionDocument.CurrentHigh"/>) simply
/// keeps Mapster's default for any mapping FROM <c>Auction</c>, which is exactly the "0 for a
/// brand-new record" behaviour <see cref="InsertIfNotExistsAsync"/> needs. Mirrors
/// SearchService's <c>ItemRepository</c> convention.
/// </summary>
public sealed class AuctionRepository(MongoDbConnection mongo) : IAuctionRepository
{
    public async Task<Auction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await mongo.Instance.Find<AuctionDocument>().OneAsync(id, cancellationToken);
        return document?.Adapt<Auction>();
    }

    public async Task InsertIfNotExistsAsync(Auction auction, CancellationToken cancellationToken)
    {
        var document = auction.Adapt<AuctionDocument>();

        // Insert-only-if-absent (phase-end code review Warning 3), NOT MongoDB.Entities'
        // SaveAsync (a whole-document upsert/replace — see IAuctionRepository's own remarks for
        // why that was wrong for a redelivered/replayed AuctionCreated). $setOnInsert with
        // IsUpsert=true is atomic at the single-document level: the filter's own equality on Id
        // means Mongo assigns _id from it automatically on insert, so every OTHER field is
        // listed explicitly here; when a document with this Id already exists, the whole
        // operation is a genuine no-op — nothing is set, matched, or touched.
        var filter = Builders<AuctionDocument>.Filter.Eq(d => d.Id, auction.Id);
        var update = Builders<AuctionDocument>.Update
            .SetOnInsert(d => d.AuctionEnd, document.AuctionEnd)
            .SetOnInsert(d => d.Seller, document.Seller)
            .SetOnInsert(d => d.ReservePrice, document.ReservePrice)
            .SetOnInsert(d => d.Finished, document.Finished)
            .SetOnInsert(d => d.CurrentHigh, document.CurrentHigh);

        await mongo.Instance.Collection<AuctionDocument>()
            .UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
    }

    public async Task<List<Auction>> GetExpiredUnfinalizedAsync(DateTime asOf, CancellationToken cancellationToken)
    {
        var documents = await mongo.Instance.Find<AuctionDocument>()
            .Match(a => !a.Finished && a.AuctionEnd <= asOf)
            .ExecuteAsync(cancellationToken);

        return documents.Select(d => d.Adapt<Auction>()).ToList();
    }

    public async Task MarkFinishedAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        // Unconditional set (see IAuctionRepository.MarkFinishedAsync's own remarks for why no
        // compare-and-swap is needed here, unlike AuctionFinalizationUnitOfWork's conditional
        // claim). A no-op — zero documents matched — when no local record exists for
        // auctionId; UpdateOneAsync does not throw for that case.
        var filter = Builders<AuctionDocument>.Filter.Eq(d => d.Id, auctionId);
        var update = Builders<AuctionDocument>.Update.Set(d => d.Finished, true);

        await mongo.Instance.Collection<AuctionDocument>()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    public async Task UpdateAuctionEndAsync(Guid auctionId, DateTime auctionEnd, CancellationToken cancellationToken)
    {
        var filter = Builders<AuctionDocument>.Filter.Eq(d => d.Id, auctionId);
        var update = Builders<AuctionDocument>.Update.Set(d => d.AuctionEnd, auctionEnd);

        await mongo.Instance.Collection<AuctionDocument>()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }
}
