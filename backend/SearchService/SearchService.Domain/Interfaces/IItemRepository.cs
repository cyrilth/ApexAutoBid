using SearchService.Domain.Entities;
using SearchService.Domain.Enums;

namespace SearchService.Domain.Interfaces;

/// <summary>
/// Entity-level repository abstraction for <see cref="Item"/> persistence in the search
/// index. Defined in Domain so that Application (the event consumers) can depend on it
/// without referencing Infrastructure. Domain has zero external NuGet dependencies — only
/// BCL and Domain entity types appear in this interface.
/// </summary>
/// <remarks>
/// Unlike <c>AuctionService</c>'s EF Core-backed <c>IAuctionRepository</c> (an
/// add/remove-then-<c>SaveChangesAsync</c> unit of work), MongoDB.Entities has no
/// change-tracking session — each operation below commits immediately. Kept to what
/// Phase 2 Task 4's consumers need (get-by-id, upsert, delete, and the atomic high-bid
/// raise for <c>BidPlaced</c>); search/filter methods land in Phase 2 Task 5.
/// </remarks>
public interface IItemRepository
{
    /// <summary>
    /// Returns the item with the given auction <paramref name="id"/>, or
    /// <see langword="null"/> if no item with that id exists in the index.
    /// </summary>
    Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new item, or wholesale-replaces the existing item with the same id.
    /// Used both for initial indexing (<c>AuctionCreated</c>) and for applying a full set
    /// of field changes fetched-then-mutated by the caller (<c>AuctionUpdated</c>,
    /// <c>AuctionFinished</c>). Safe to call repeatedly with the same id and the same
    /// values — redelivery is a no-op overwrite, not a duplicate insert.
    /// </summary>
    Task UpsertAsync(Item item, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the item with the given auction <paramref name="id"/> from the index.
    /// Deleting an id that does not exist (or no longer exists, e.g. redelivered
    /// <c>AuctionDeleted</c>) succeeds silently rather than throwing.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically raises the item's <c>CurrentHighBid</c> to <paramref name="amount"/>, but
    /// only when the item is still <c>Live</c> and <paramref name="amount"/> strictly
    /// exceeds the stored high bid (or the stored high bid is unset). The predicate is
    /// evaluated by MongoDB in a single conditional update, so concurrent <c>BidPlaced</c>
    /// messages for the same item cannot lose updates and redelivery of an older/equal bid
    /// is a no-op. Also stamps <c>UpdatedAt</c>.
    /// </summary>
    Task<HighBidUpdateResult> TryRaiseHighBidAsync(Guid id, int amount, CancellationToken cancellationToken);
}
