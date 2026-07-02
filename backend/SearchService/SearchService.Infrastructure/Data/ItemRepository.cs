using Mapster;
using MongoDB.Entities;
using SearchService.Domain.Entities;
using SearchService.Domain.Enums;
using SearchService.Domain.Interfaces;

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
}
