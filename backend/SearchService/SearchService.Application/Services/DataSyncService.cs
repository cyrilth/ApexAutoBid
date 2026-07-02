using MapsterMapper;
using Microsoft.Extensions.Logging;
using SearchService.Application.DTOs;
using SearchService.Domain.Entities;
using SearchService.Domain.Interfaces;

namespace SearchService.Application.Services;

/// <summary>
/// <see cref="IDataSyncService"/> implementation for the Phase 2 Task 6 HTTP polling
/// fallback.
/// </summary>
/// <remarks>
/// <para>
/// <b>Backstop rationale:</b> this is the documented backstop for two accepted event-consumer
/// limitations — <c>AuctionCreatedConsumer</c>'s AuctionDeleted-before-AuctionCreated
/// resurrection window, and <c>AuctionUpdatedConsumer</c>'s lost-updates-under-reordering. For
/// that backstop claim to mean anything, the reconciliation here re-syncs the FULL document
/// (every <see cref="Item"/> field — Status/Winner/CurrentHighBid included, not just the base
/// item fields), via <see cref="IItemRepository.UpsertAsync"/>'s existing full-replace
/// semantics (<c>ItemRepository.UpsertAsync</c> → <c>DB.SaveAsync</c>, a complete
/// insert-or-replace) — see <c>ItemMappingConfig</c>'s <c>AuctionSyncDto → Item</c> rule for
/// the exhaustive field mapping.
/// </para>
/// <para>
/// <b>Failure policy:</b> failures are contained at two levels so that nothing thrown by this
/// sync can ever prevent startup (Program.cs additionally wraps the call to this method as a
/// last line of defense — see its own comment). At the HTTP level: if the Auction Service is
/// unreachable after the resilience pipeline (<c>AuctionServiceHttpClient</c>'s standard
/// resilience handler — retries, circuit breaker, timeouts) is exhausted,
/// <see cref="IAuctionServiceClient.GetAuctionsFromDateAsync"/> returns <see langword="null"/>
/// and this method logs an error and returns without throwing. At the per-item level: mapping
/// or upserting any single auction (e.g. a malformed record — upstream sending a null
/// <c>Images</c> array where <c>AuctionSyncDto</c>'s default is an empty list — or a
/// transient Mongo write hiccup on just that one document) is wrapped so one bad record is
/// logged and skipped rather than aborting the rest of the batch — see the skip-and-continue
/// loop below. The sync is a fallback, not a hard dependency: this service must still start,
/// serve <c>GET api/search</c> traffic, and consume events even when the Auction Service is
/// down or a single record is bad (independent-service resilience) — and because this sync
/// re-runs on every restart, letting any exception escape here would mean one persistently
/// bad record permanently prevents startup, not just one sync cycle.
/// </para>
/// <para>
/// <b>Residual gap — this cannot remove orphans:</b> the delta query (<c>UpdatedAt &gt;
/// date</c>) is fundamentally blind to deletions. An item removed upstream while this service
/// was down, or one resurrected by the AuctionCreatedConsumer race described above, has no
/// "updated" upstream record to report it — it is invisible to this sync no matter how often
/// it runs. Even a full resync (passing <see langword="null"/> for <c>updatedAfter</c>, e.g.
/// against an empty index) reconciles every field of every auction that still exists
/// upstream, but still cannot detect one that no longer does. Closing that gap needs a full
/// id-diff (enumerate every upstream id, delete any indexed id absent from that set) — out of
/// scope for this task.
/// </para>
/// <para>
/// <b>Residual gap — a never-indexed auction can permanently age out of the delta window:</b>
/// distinct from the deletion gap above. If a given auction's <c>AuctionCreated</c> event is
/// irrecoverably lost (e.g. it exhausts MassTransit's retries and lands in the endpoint's
/// <c>_error</c> queue) and this sync never happens to run before that loss, the auction is
/// never indexed at all. Once some OTHER, newer auction pushes
/// <see cref="IItemRepository.GetLatestUpdatedAtAsync"/>'s high-water mark past that auction's
/// own <c>UpdatedAt</c>, every future delta sync (<c>UpdatedAt &gt; date</c>, evaluated
/// against the ever-advancing max) permanently excludes it too — it's too old to match the
/// filter, and it never becomes "updated" again unless someone touches it upstream. Recovery
/// paths: a full resync (this only naturally happens against an empty index, so today that
/// means clearing the index or otherwise resetting <c>GetLatestUpdatedAtAsync</c>'s source
/// data), a fresh upstream update to that specific auction (any <c>AuctionUpdated</c> bumps
/// its <c>UpdatedAt</c> back above the current high-water mark), or the future full-id-diff
/// reconciliation described in the deletion gap above (which would also close this one, since
/// it doesn't depend on <c>UpdatedAt</c> at all). No behavior change here — documented as a
/// known limitation.
/// </para>
/// <para>
/// <b>Bid-driven changes ARE covered by delta syncs:</b> verified against
/// <c>AuctionService.Infrastructure.Data.AuctionRepository.TryRaiseHighBidAsync</c> — its
/// atomic conditional update does <c>SetProperty(a =&gt; a.UpdatedAt, DateTime.UtcNow)</c>
/// alongside <c>CurrentHighBid</c>, so a bid that raises the high bid also bumps
/// <c>Auction.UpdatedAt</c> and therefore surfaces in the next delta sync — bid updates are
/// not solely dependent on the <c>BidPlaced</c> event being consumed.
/// </para>
/// <para>
/// <b>Observed — millisecond DateTime precision widens the delta boundary (harmless):</b>
/// MongoDB's <c>DateTime</c> (BSON) is millisecond-precision, while AuctionService's Postgres
/// <c>UpdatedAt</c> retains microsecond precision. <see cref="IItemRepository.GetLatestUpdatedAtAsync"/>
/// therefore returns a value truncated (rounded down) to the millisecond, so the upstream
/// <c>UpdatedAt &gt; date</c> comparison can still match rows whose real timestamp is only a
/// fraction of a millisecond newer than what's stored here — observed in practice to reliably
/// re-match an entire batch of auctions seeded within the same millisecond. This makes the
/// delta sync over-inclusive near the boundary, never under-inclusive, and is harmless: the
/// same full-document-replace upsert that makes this method idempotent against redelivery
/// also makes redundant re-syncing of unchanged rows a no-op rather than a duplicate or a
/// correctness issue — just some avoidable work on a once-at-startup call.
/// </para>
/// </remarks>
public class DataSyncService(
    IAuctionServiceClient client,
    IItemRepository repository,
    IMapper mapper,
    ILogger<DataSyncService> logger) : IDataSyncService
{
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var latestUpdatedAt = await repository.GetLatestUpdatedAtAsync(cancellationToken);

        logger.LogInformation(
            "Starting Auction Service sync (updatedAfter: {UpdatedAfter})",
            latestUpdatedAt?.ToString("o") ?? "(none — full sync)");

        var auctions = await client.GetAuctionsFromDateAsync(latestUpdatedAt, cancellationToken);

        if (auctions is null)
        {
            // See this class's XML remarks ("Failure policy") — deliberately not a throw.
            logger.LogError(
                "Auction Service sync failed — the Auction Service could not be reached " +
                "after retries. Continuing startup; the search index may be stale until the " +
                "next successful sync or until events catch it up.");
            return;
        }

        var synced = 0;
        var skipped = 0;

        foreach (var auction in auctions)
        {
            try
            {
                var item = mapper.Map<Item>(auction);
                await repository.UpsertAsync(item, cancellationToken);
                synced++;
            }
            catch (Exception ex)
            {
                // Per-item containment (see "Failure policy" remarks): one malformed record
                // or one transient Mongo write hiccup must not abort the rest of the batch —
                // log with the auction id and keep going, same skip-and-continue spirit as
                // the HTTP-level failure policy above.
                skipped++;
                logger.LogError(ex, "Failed to sync auction {AuctionId} — skipping", auction.Id);
            }
        }

        logger.LogInformation(
            "Auction Service sync complete — synced {Synced}, skipped {Skipped}", synced, skipped);
    }
}
