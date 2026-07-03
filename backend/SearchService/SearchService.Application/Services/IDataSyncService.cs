namespace SearchService.Application.Services;

/// <summary>
/// Orchestrates the Phase 2 Task 6 HTTP polling fallback: a once-at-startup sync from the
/// Auction Service that seeds/repairs the search index before this service starts consuming
/// events (see <c>Program.cs</c>'s ordering comment for why sync-before-bus-start matters).
/// </summary>
public interface IDataSyncService
{
    /// <summary>
    /// Fetches every auction updated since the index's own latest <c>UpdatedAt</c> (a full
    /// sync when the index is empty) and upserts each as a full-document replace. See the
    /// implementation's XML doc for the failure policy, the backstop rationale, and the
    /// residual reconciliation gap this leaves.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken);
}
