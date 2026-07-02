using SearchService.Domain.Entities;
using SearchService.Domain.Enums;
using SearchService.Domain.Models;

namespace SearchService.Domain.Interfaces;

/// <summary>
/// Entity-level repository abstraction for <see cref="Item"/> persistence in the search
/// index. Defined in Domain so that Application (the event consumers and the search
/// service) can depend on it without referencing Infrastructure. Domain has zero external
/// NuGet dependencies — only BCL and Domain entity types appear in this interface.
/// </summary>
/// <remarks>
/// Unlike <c>AuctionService</c>'s EF Core-backed <c>IAuctionRepository</c> (an
/// add/remove-then-<c>SaveChangesAsync</c> unit of work), MongoDB.Entities has no
/// change-tracking session — each operation below commits immediately.
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

    /// <summary>
    /// Returns a page of items matching <paramref name="query"/> (Phase 2 Task 5 — <c>GET
    /// api/search</c>). <paramref name="query"/> is already fully validated/normalized by
    /// the caller (<c>SearchAppService</c>) — this method only translates it into a MongoDB
    /// query and never itself reads the wall clock (see <see cref="ItemSearchQuery"/>'s
    /// remarks on why <c>Now</c> travels in the query).
    /// </summary>
    Task<PagedItems> SearchAsync(ItemSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the maximum <c>UpdatedAt</c> across every indexed item, or <see
    /// langword="null"/> when the index is empty. Used by <c>DataSyncService</c> (Phase 2
    /// Task 6) to ask the Auction Service for only what changed since the last successful
    /// sync (<c>GET api/auctions?date=</c>), rather than re-pulling the entire catalog every
    /// startup.
    /// </summary>
    Task<DateTime?> GetLatestUpdatedAtAsync(CancellationToken cancellationToken);
}
