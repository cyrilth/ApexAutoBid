using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;
using BiddingService.Domain.Interfaces;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// <see cref="IBidRepository"/> implementation backed by MongoDB.Entities against
/// <see cref="BidDocument"/> — read-only (see <see cref="IBidRepository"/>'s remarks for why
/// the write path used at bid-placement time goes through
/// <see cref="BidPlacementUnitOfWork"/> instead).
/// </summary>
public sealed class BidRepository(MongoDbConnection mongo) : IBidRepository
{
    public async Task<Bid?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await mongo.Instance.Find<BidDocument>().OneAsync(id, cancellationToken);
        return document is null ? null : BidDocumentMapper.ToDomain(document);
    }

    public async Task<List<Bid>> GetByAuctionIdAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        var documents = await mongo.Instance.Find<BidDocument>()
            .Match(b => b.AuctionId == auctionId)
            // Newest first; Id ascending is a deterministic tiebreaker for bids recorded in
            // the same tick (mirrors SearchService's ItemRepository.SearchAsync convention).
            .Sort(b => b.BidTime, Order.Descending)
            .Sort(b => b.Id, Order.Ascending)
            .ExecuteAsync(cancellationToken);

        return documents.Select(BidDocumentMapper.ToDomain).ToList();
    }

    public async Task<int?> GetHighestAcceptedAmountAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        // Only Accepted/AcceptedBelowReserve represent a genuine current high bid — exact
        // string comparison against the enum's own .ToString() names (BidDocument.BidStatus
        // is persisted that way — see BidDocumentMapper), mirroring
        // AuctionService/SearchService's BidPlacedConsumer's identical status filter.
        //
        // Amount desc, then BidTime asc (first bidder wins a tie), then Id — a deterministic
        // tiebreak (see IBidRepository's own remarks for why a genuine Amount tie can no longer
        // arise via the atomic accept path, and why the tiebreak still exists defensively).
        var top = await mongo.Instance.Find<BidDocument>()
            .Match(b => b.AuctionId == auctionId &&
                (b.BidStatus == nameof(BidStatus.Accepted) || b.BidStatus == nameof(BidStatus.AcceptedBelowReserve)))
            .Sort(b => b.Amount, Order.Descending)
            .Sort(b => b.BidTime, Order.Ascending)
            .Sort(b => b.Id, Order.Ascending)
            .Limit(1)
            .ExecuteFirstAsync(cancellationToken);

        return top?.Amount;
    }

    public async Task<Bid?> GetHighestAcceptedBidAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        // Strictly "Accepted" — excludes "AcceptedBelowReserve" (see this method's own XML doc
        // on IBidRepository for why finalization must not treat those as a sale). Covered by
        // the same compound (AuctionId, BidStatus, Amount desc) index EnsureIndexesAsync
        // already creates for GetHighestAcceptedAmountAsync — an equality filter on one exact
        // BidStatus value is still a prefix match against that index. Same deterministic
        // tiebreak as GetHighestAcceptedAmountAsync above.
        var top = await mongo.Instance.Find<BidDocument>()
            .Match(b => b.AuctionId == auctionId && b.BidStatus == nameof(BidStatus.Accepted))
            .Sort(b => b.Amount, Order.Descending)
            .Sort(b => b.BidTime, Order.Ascending)
            .Sort(b => b.Id, Order.Ascending)
            .Limit(1)
            .ExecuteFirstAsync(cancellationToken);

        return top is null ? null : BidDocumentMapper.ToDomain(top);
    }
}
