using MongoDB.Entities;

namespace SearchService.Infrastructure.Data;

/// <summary>
/// Holds the connected <see cref="DB"/> instance that <c>DB.InitAsync</c> returns.
/// <para>
/// MongoDB.Entities 25.1.0 exposes its query/write builders (<c>Find</c>, <c>Update</c>,
/// <c>Index</c>, ...) and simple operations (<c>SaveAsync</c>, <c>DeleteAsync</c>) as
/// <b>instance</b> members on <c>DB</c> rather than static passthroughs to an ambient
/// default database — there is no bare <c>DB.Find&lt;T&gt;()</c> to call. The single
/// connected <c>DB</c> instance is therefore created once at startup
/// (<see cref="DbInitializer.InitDbAsync"/>) and threaded through this singleton holder to
/// anything that talks to Mongo afterwards (<see cref="ItemRepository"/>), instead of each
/// component re-resolving it via a service locator.
/// </para>
/// </summary>
public sealed class MongoDbContext
{
    private DB? _db;

    /// <summary>
    /// The connected <see cref="DB"/> instance. Throws if accessed before
    /// <see cref="DbInitializer.InitDbAsync"/> has run — every real code path reaches this
    /// only after startup has completed.
    /// </summary>
    /// <remarks>
    /// Correctness here already relies on a program-order invariant: Program.cs awaits
    /// <c>DbInitializer.InitDbAsync</c> (which calls <see cref="SetInstance"/>) to completion
    /// before <c>app.Run()</c> starts the MassTransit bus that drives consumers into
    /// <see cref="Instance"/>. <see cref="Volatile.Read{T}"/>/<see cref="Volatile.Write{T}"/>
    /// are used anyway (instead of a plain field) so that invariant isn't also required to
    /// carry the memory-visibility guarantee across threads — cheap insurance against a
    /// future refactor that starts consumers concurrently with initialization.
    /// </remarks>
    public DB Instance =>
        Volatile.Read(ref _db) ?? throw new InvalidOperationException(
            $"{nameof(MongoDbContext)}.{nameof(Instance)} was accessed before " +
            $"{nameof(DbInitializer)}.{nameof(DbInitializer.InitDbAsync)} completed.");

    /// <summary>Called exactly once, by <see cref="DbInitializer.InitDbAsync"/>.</summary>
    internal void SetInstance(DB db) => Volatile.Write(ref _db, db);
}
