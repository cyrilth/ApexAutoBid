using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// Holds the connected MongoDB.Entities <see cref="DB"/> instance that
/// <see cref="DbInitializer.InitDbAsync"/> creates, for this service's read-side repositories
/// (<see cref="BidRepository"/>/<see cref="AuctionRepository"/>) — mirrors
/// <c>SearchService.Infrastructure.Data.MongoDbContext</c>'s identical purpose and XML doc.
/// <para>
/// <b>Deliberately named "MongoDbConnection", not "MongoDbContext"</b> (unlike SearchService's
/// identically-purposed type): this service also depends on
/// <c>MassTransit.MongoDbIntegration.MongoDbContext</c> — a completely different, MassTransit-owned
/// scoped type that <see cref="BidPlacementUnitOfWork"/> uses for the transactional bus outbox
/// (Requirements §3.3 / Task 4). Reusing the name "MongoDbContext" here would force every file
/// referencing either type to fully qualify one of them; a distinct name avoids that ambiguity
/// entirely. The two are otherwise unrelated: this one is a process-lifetime singleton
/// wrapping MongoDB.Entities' own driver session (used for plain reads); that one is a
/// per-request <em>scoped</em> type MassTransit registers specifically for its bus-outbox
/// transaction (used only for the one write path that must be atomic with a
/// <c>BidPlaced</c> publish).
/// </para>
/// </summary>
public sealed class MongoDbConnection
{
    private DB? _db;

    /// <summary>
    /// The connected <see cref="DB"/> instance. Throws if accessed before
    /// <see cref="DbInitializer.InitDbAsync"/> has run — every real code path reaches this
    /// only after startup has completed (see <c>MongoDbContext.Instance</c> in SearchService
    /// for the identical memory-visibility rationale for <see cref="Volatile"/>).
    /// </summary>
    public DB Instance =>
        Volatile.Read(ref _db) ?? throw new InvalidOperationException(
            $"{nameof(MongoDbConnection)}.{nameof(Instance)} was accessed before " +
            $"{nameof(DbInitializer)}.{nameof(DbInitializer.InitDbAsync)} completed.");

    /// <summary>Called exactly once, by <see cref="DbInitializer.InitDbAsync"/>.</summary>
    internal void SetInstance(DB db) => Volatile.Write(ref _db, db);
}
