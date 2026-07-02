using Mapster;
using MongoDB.Entities;
using SearchService.Domain.Entities;
using SearchService.Domain.Enums;
using SearchService.Domain.Interfaces;
using SearchService.Domain.Models;

namespace SearchService.Infrastructure.Data;

/// <summary>
/// <see cref="IItemRepository"/> implementation backed by MongoDB.Entities against
/// <see cref="ItemDocument"/>. Maps between the Domain <see cref="Item"/> and the
/// persistence-layer <see cref="ItemDocument"/> with a plain, config-free
/// <c>.Adapt&lt;T&gt;()</c> call — every field on the two types matches by name and type
/// (see <see cref="ItemDocument"/>'s XML doc), so Mapster's by-convention member copy needs
/// no <c>TypeAdapterConfig</c> rules. This does not use the DI-scoped
/// <c>TypeAdapterConfig</c>/<c>IMapper</c> pattern used elsewhere in the codebase (see
/// <c>AuctionMappingConfig</c>'s remarks) because <see cref="ItemDocument"/> is
/// Infrastructure-only and can never appear in an Application-layer <c>IRegister</c>.
/// </summary>
public sealed class ItemRepository(MongoDbContext mongo) : IItemRepository
{
    public async Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await mongo.Instance.Find<ItemDocument>().OneAsync(id, cancellationToken);
        return document?.Adapt<Item>();
    }

    public async Task UpsertAsync(Item item, CancellationToken cancellationToken)
    {
        var document = item.Adapt<ItemDocument>();
        await mongo.Instance.SaveAsync(document, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // DeleteAsync matching zero documents (id already gone, or never existed) completes
        // without error — exactly the silent-success idempotency IItemRepository documents.
        await mongo.Instance.DeleteAsync<ItemDocument>(id, cancellationToken);
    }

    public async Task<HighBidUpdateResult> TryRaiseHighBidAsync(
        Guid id, int amount, CancellationToken cancellationToken)
    {
        var result = await mongo.Instance.Update<ItemDocument>()
            .Match(x =>
                x.Id == id &&
                x.Status == "Live" &&
                (x.CurrentHighBid == null || x.CurrentHighBid < amount))
            .Modify(x => x.CurrentHighBid, amount)
            .Modify(x => x.UpdatedAt, DateTime.UtcNow)
            .ExecuteAsync(cancellationToken);

        if (result.ModifiedCount > 0)
            return HighBidUpdateResult.Raised;

        // The conditional update matched nothing — either the item doesn't exist at all, or
        // it exists but the bid didn't qualify (not Live, or didn't beat the stored high
        // bid). Distinguish the two with a cheap existence check so callers can tell a
        // cross-service anomaly (ItemNotFound) apart from the benign, expected no-op.
        var exists = await mongo.Instance.Find<ItemDocument>().OneAsync(id, cancellationToken) is not null;
        return exists ? HighBidUpdateResult.NotRaised : HighBidUpdateResult.ItemNotFound;
    }

    /// <remarks>
    /// <b>$text must be the first aggregation pipeline stage:</b> when <c>Search.Full</c> is
    /// used below, MongoDB.Entities' <c>PagedSearch.ExecuteAsync</c> builds a pipeline shaped
    /// <c>[{ $match: ... }, { $facet: { data: [...], totalCount: [...] } }]</c> — every
    /// <c>.Match()</c> call (text or otherwise) accumulates into that single leading
    /// <c>$match</c> stage, so the text search is structurally always first. This method
    /// never needs to special-case ordering itself.
    /// </remarks>
    public async Task<PagedItems> SearchAsync(ItemSearchQuery query, CancellationToken cancellationToken)
    {
        // The 2-generic-arg overload is used explicitly (rather than the PagedSearch<T>
        // shorthand) because every fluent method (.Match/.Sort/...) returns
        // PagedSearch<T, TProjection>, not PagedSearch<T> — declaring the variable with that
        // type up front avoids a type mismatch on every reassignment below.
        var search = mongo.Instance.PagedSearch<ItemDocument, ItemDocument>();

        // Full-text search against the existing compound Make/Model/Color text index
        // (DbInitializer.EnsureIndexesAsync) — never build a second text index (MongoDB
        // allows only one per collection).
        if (query.SearchTerm is { } searchTerm)
            search = search.Match(Search.Full, searchTerm);

        // Each of these calls to .Match() ANDs an additional predicate onto the query
        // (MongoDB.Entities accumulates successive .Match() calls) — kept as separate,
        // conditionally-added calls rather than one combined expression so we never rely on
        // the MongoDB LINQ provider partially evaluating captured local variables inside an
        // expression tree.
        if (query.Seller is { } seller)
            search = search.Match(x => x.Seller == seller);

        if (query.Winner is { } winner)
            search = search.Match(x => x.Winner == winner);

        var now = query.Now;

        switch (query.FilterBy)
        {
            case ItemFilterBy.Live:
                search = search.Match(x => x.Status == "Live" && x.AuctionEnd > now);
                break;

            case ItemFilterBy.EndingSoon:
                var endingSoonCutoff = now + ItemSearchDefaults.EndingSoonWindow;
                search = search.Match(x =>
                    x.Status == "Live" && x.AuctionEnd > now && x.AuctionEnd < endingSoonCutoff);
                break;

            case ItemFilterBy.Finished:
                // Deliberate tradeoff: this $or ($lte on AuctionEnd OR $ne on Status) largely
                // defeats simple single-field index selectivity — Mongo can't serve an $or
                // across two different fields with one index the way it could a single range
                // predicate. Accepted because "finished" result sets are naturally large
                // (unbounded historical data) where full selectivity matters less, and
                // AllowDiskUse below covers the resulting sort if it can't stay in memory.
                search = search.Match(x => x.AuctionEnd <= now || x.Status != "Live");
                break;

            case ItemFilterBy.All:
            default:
                // No lifecycle constraint — see ItemFilterBy.All's XML doc.
                break;
        }

        search = query.OrderBy switch
        {
            ItemOrderBy.Make => search.Sort(x => x.Make, Order.Ascending).Sort(x => x.Model, Order.Ascending),
            ItemOrderBy.New => search.Sort(x => x.CreatedAt, Order.Descending),
            _ => search.Sort(x => x.AuctionEnd, Order.Ascending) // ItemOrderBy.EndingSoon (default)
        };

        // PagedSearch throws if no Sort is specified — the switch above always adds at least
        // one. Id ascending is always appended last as a final tiebreaker so paging stays
        // deterministic even when many items share the same primary sort key value.
        search = search.Sort(x => x.Id, Order.Ascending);

        search = search.PageNumber(query.PageNumber).PageSize(query.PageSize);

        // Belt-and-braces: DbInitializer provisions compound indexes matching every orderBy's
        // exact sort-key sequence, but some filter+sort combinations the query planner can't
        // fully satisfy from an index (e.g. seller=X combined with the endingSoon filter,
        // whose bounds aren't part of any index) would otherwise hit the in-memory $sort
        // stage's 100MB limit and throw. AllowDiskUse lets Mongo spill to disk instead.
        search = search.Option(o => o.AllowDiskUse = true);

        var (results, totalCount, pageCount) = await search.ExecuteAsync(cancellationToken);

        return new PagedItems
        {
            Results = results.Select(document => document.Adapt<Item>()).ToList(),
            TotalCount = totalCount,
            PageCount = pageCount
        };
    }

    public async Task<DateTime?> GetLatestUpdatedAtAsync(CancellationToken cancellationToken)
    {
        // Deliberately unindexed: DbInitializer does not provision an UpdatedAt-descending
        // index for this. Unlike GET api/search's indexes (hit on every user request), this
        // query runs exactly once at startup (DataSyncService) against what will realistically
        // stay a small collection — an occasional collection scan here is an acceptable
        // tradeoff against carrying a fourth single-purpose index.
        var latest = await mongo.Instance.Find<ItemDocument>()
            .Sort(x => x.UpdatedAt, Order.Descending)
            .Limit(1)
            .ExecuteFirstAsync(cancellationToken);

        return latest?.UpdatedAt;
    }
}
