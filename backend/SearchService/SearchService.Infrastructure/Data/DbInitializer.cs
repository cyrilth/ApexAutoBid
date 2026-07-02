using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Entities;

namespace SearchService.Infrastructure.Data;

/// <summary>
/// Handles MongoDB connection initialization and index creation on application startup.
/// Call <c>await DbInitializer.InitDbAsync(app.Services)</c> from the API's
/// <c>Program.cs</c> immediately after <c>var app = builder.Build();</c>.
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// Database name per the database-per-service table (<c>Docs/Architecture.md</c> §4.1) —
    /// Search Service owns the <c>search</c> MongoDB database. Internal (not private) so
    /// <c>InfrastructureServiceExtensions</c>'s <c>IMongoDatabase</c> registration (Phase 2
    /// Task 7's Mongo outbox) points at the exact same database name without a second
    /// hardcoded <c>"search"</c> literal drifting from this one.
    /// </summary>
    internal const string DatabaseName = "search";

    /// <summary>
    /// Bounded startup retry window for the initial Mongo connect (Phase 2 Task 8 —
    /// Dockerize the Search Service): 10 attempts, 3 seconds apart (~27s total), covering
    /// the common case of this service's container starting before the mongodb container is
    /// actually ready to accept connections. Deliberately does NOT wrap index creation
    /// (<see cref="EnsureIndexesAsync"/>) — that only ever runs after a successful connect,
    /// so it has nothing to retry against.
    /// </summary>
    private const int MaxConnectAttempts = 10;

    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(3);

    public static async Task InitDbAsync(
        IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        // ILogger<T> can't take a static class as its type argument, so resolve the
        // category from the factory to keep these logs under the DbInitializer category
        // (not an arbitrary entity type's).
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializer));

        var connectionString = config.GetConnectionString("MongoDbConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:MongoDbConnection is not configured");

        // The connection string carries ?directConnection=true (Phase 2 Task 7): apex-mongodb
        // is a single-node replica set (required for MassTransit's Mongo outbox/inbox
        // transactions — see docker-compose.infra.yml's mongodb service comment for the full
        // why). Without that flag, the driver's replica-set discovery mode would try to
        // resolve the set's member(s) by their advertised hostname ("localhost:27017" in dev,
        // "mongodb:27017" in-container — per the healthcheck's rs.initiate() call), which is
        // fine inside the Docker network the member itself is reachable on, but doesn't
        // reliably resolve the same way from the Windows host running `dotnet run`, nor is it
        // guaranteed to match how a *different* container reaches this one by service name;
        // directConnection=true instead talks to exactly the one node in the connection
        // string directly, skipping discovery entirely, in every environment. This same
        // connection string is reused verbatim for the IMongoClient/IMongoDatabase registered
        // in InfrastructureServiceExtensions for the Mongo outbox.
        var db = await ConnectWithRetryAsync(connectionString, logger, cancellationToken);

        // MongoDB.Entities 25.1.0 exposes Find/Save/Delete/Update/Index as instance members
        // on the connected DB instance, not static passthroughs — hand it to the singleton
        // holder so ItemRepository (and anything else added later) can reach it without a
        // service locator. See MongoDbContext's XML doc for the full rationale.
        scope.ServiceProvider.GetRequiredService<MongoDbContext>().SetInstance(db);

        await EnsureIndexesAsync(db, logger);
    }

    /// <summary>
    /// Connects to MongoDB, retrying up to <see cref="MaxConnectAttempts"/> times
    /// (<see cref="ConnectRetryDelay"/> apart) before giving up. Each failed attempt is
    /// logged at Warning with its attempt number; the final attempt's exception is left to
    /// propagate uncaught.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately fails hard (rethrows, crashing startup) once the window is exhausted —
    /// unlike <c>DataSyncService</c>'s HTTP polling fallback (Task 6), which degrades
    /// gracefully and lets startup continue when the Auction Service is unreachable. That's
    /// the right call there because the Auction Service is a DIFFERENT service this one only
    /// depends on for a best-effort baseline sync. MongoDB is this service's OWN datastore:
    /// every <c>GET api/search</c> request and every event consumer ultimately reads or
    /// writes it, so there is no meaningful "degraded but running" state without it —
    /// retrying forever would just hide a genuine configuration/infrastructure problem
    /// instead of surfacing it, and container orchestrators are built to restart a process
    /// that exits non-zero, which is exactly what an uncaught exception here produces.
    /// </para>
    /// <para>
    /// <b>ServerSelectionTimeout = 5s (Task 8 code review):</b> the driver's own default is
    /// 30s, which would make the worst case here ~5.5 minutes (10 × 30s + 9 × 3s) before this
    /// process exits — too slow for a container orchestrator's crash-loop feedback. Trimming
    /// it to 5s brings the worst case down to ~80s (10×5s + 9×3s). CAVEAT: this setting lives
    /// on the <see cref="MongoClientSettings"/> object baked into the returned <see cref="DB"/>
    /// instance for its whole lifetime (MongoDB.Entities/the driver caches the underlying
    /// client by settings value-equality), so every RUNTIME query this service makes afterward
    /// also gets only a 5s server-selection budget, not just this startup connect. Acceptable
    /// today because the topology is a single node — a failure hits the consumer retry policy
    /// or the request pipeline quickly either way — but revisit (raise it, or split startup vs.
    /// runtime settings) if a future multi-node replica set needs failover-election headroom,
    /// which typically wants more than 10s.
    /// </para>
    /// <para>
    /// <b>Cancellation:</b> <paramref name="cancellationToken"/> is only observed by the
    /// <see cref="Task.Delay(TimeSpan,CancellationToken)"/> between attempts — <c>DB.InitAsync</c>
    /// itself takes no token, so each attempt is a non-cancellable chunk bounded by the 5s
    /// ServerSelectionTimeout above. This is defense-in-depth, not a guarantee: it only helps
    /// hosts where the lifetime's shutdown signal is already wired up when this runs (e.g.
    /// tests, or a future hosting change) — <c>Program.cs</c> calls this before
    /// <c>app.Run()</c>, and the generic host's console lifetime doesn't register its
    /// Ctrl+C/SIGTERM handlers until <c>Run()</c>/<c>StartAsync()</c> actually starts, so
    /// pre-<c>Run()</c> signal delivery here is best-effort at best today.
    /// </para>
    /// </remarks>
    private static async Task<DB> ConnectWithRetryAsync(
        string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        var clientSettings = MongoClientSettings.FromConnectionString(connectionString);
        clientSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            try
            {
                logger.LogInformation(
                    "Connecting to MongoDB database {DatabaseName} (attempt {Attempt}/{MaxAttempts})",
                    DatabaseName, attempt, MaxConnectAttempts);
                var db = await DB.InitAsync(DatabaseName, clientSettings);
                logger.LogInformation(
                    "MongoDB connection initialized for database {DatabaseName}", DatabaseName);
                return db;
            }
            catch (Exception ex) when (attempt < MaxConnectAttempts)
            {
                // The `when (attempt < MaxConnectAttempts)` guard means the LAST attempt's
                // exception is deliberately NOT caught here — it propagates straight out of
                // this method (see the XML remarks above for why that's correct).
                logger.LogWarning(ex,
                    "MongoDB connection attempt {Attempt}/{MaxAttempts} failed — retrying in {Delay}",
                    attempt, MaxConnectAttempts, ConnectRetryDelay);
                await Task.Delay(ConnectRetryDelay, cancellationToken);
            }
        }

        // Unreachable: every loop iteration either returns on success or (on the final
        // attempt) lets its exception propagate uncaught. This satisfies the compiler's
        // control-flow analysis, which can't see that the loop never falls through.
        throw new UnreachableException(
            $"{nameof(ConnectWithRetryAsync)} loop completed without returning or throwing.");
    }

    private static async Task EnsureIndexesAsync(DB db, ILogger logger)
    {
        logger.LogInformation(
            "Ensuring text index on {Collection} ({Fields})", "Items", "Make, Model, Color");

        // MongoDB allows only ONE text index per collection. Changing this field list later
        // (e.g. Task 5 adding more searchable fields) is not a matter of just editing the
        // .Key() calls below — the existing "Items" text index must be dropped explicitly
        // first (DB.Index<ItemDocument>().DropAsync(name) / DropAllAsync()), or CreateAsync
        // will throw on startup because a second text index can't coexist with the first.
        await db.Index<ItemDocument>()
            .Key(x => x.Make, KeyType.Text)
            .Key(x => x.Model, KeyType.Text)
            .Key(x => x.Color, KeyType.Text)
            .CreateAsync();

        logger.LogInformation("Text index ready on {Collection}", "Items");

        // ── Equality-filter single-field indexes ──────────────────────────────────
        //
        // GET api/search filters on Seller/Winner/Status by exact equality — a single-field
        // ascending index serves each of those independently regardless of which other
        // params are supplied.
        logger.LogInformation("Ensuring single-field indexes on {Collection}", "Items");

        await db.Index<ItemDocument>().Key(x => x.Seller, KeyType.Ascending).CreateAsync();
        await db.Index<ItemDocument>().Key(x => x.Winner, KeyType.Ascending).CreateAsync();
        await db.Index<ItemDocument>().Key(x => x.Status, KeyType.Ascending).CreateAsync();

        logger.LogInformation("Single-field indexes ready on {Collection}", "Items");

        // ── Compound sort indexes (Task 5 code review carry-forward) ─────────────
        //
        // GET api/search's three orderBy branches each produce a multi-key $sort — Make
        // asc/Model asc/Id asc, AuctionEnd asc/Id asc, or CreatedAt desc/Id asc (Id is always
        // appended as the deterministic paging tiebreaker; see ItemRepository.SearchAsync).
        // A single-field index cannot serve a multi-key sort, so without a compound index
        // matching the exact key sequence, Mongo falls back to an in-memory $sort (100MB
        // limit — ItemRepository.SearchAsync also sets AllowDiskUse as a second line of
        // defense). These compound indexes replace the single-field Make/AuctionEnd/
        // CreatedAt indexes from the initial Task 5 pass — now redundant, since a compound
        // index's leading field still serves any plain equality/range use of that field
        // alone — see DropObsoleteIndexesAsync below.
        logger.LogInformation("Ensuring compound sort indexes on {Collection}", "Items");

        await db.Index<ItemDocument>()
            .Key(x => x.Make, KeyType.Ascending)
            .Key(x => x.Model, KeyType.Ascending)
            .Key(x => x.Id, KeyType.Ascending)
            .CreateAsync();

        await db.Index<ItemDocument>()
            .Key(x => x.AuctionEnd, KeyType.Ascending)
            .Key(x => x.Id, KeyType.Ascending)
            .CreateAsync();

        await db.Index<ItemDocument>()
            .Key(x => x.CreatedAt, KeyType.Descending)
            .Key(x => x.Id, KeyType.Ascending)
            .CreateAsync();

        logger.LogInformation("Compound sort indexes ready on {Collection}", "Items");

        await DropObsoleteIndexesAsync(db, logger);
    }

    /// <summary>
    /// Drops the single-field Make/AuctionEnd/CreatedAt indexes created by the initial
    /// Task 5 pass, now superseded by the compound sort indexes above. MongoDB.Entities
    /// never drops an index automatically just because the code that created it changed, so
    /// a dev database that already ran the old startup logic would otherwise keep these
    /// obsolete indexes forever. Guarded by an existence check via the raw driver collection
    /// (rather than an unconditional <c>DropAsync</c>) so this stays idempotent — a fresh
    /// database that never created them must not throw <c>IndexNotFound</c>.
    /// </summary>
    private static async Task DropObsoleteIndexesAsync(DB db, ILogger logger)
    {
        // Auto-generated MongoDB.Entities index names follow the "{Field}(Asc|Desc)" pattern
        // — confirmed against this collection's actual index list before this fix shipped.
        string[] obsoleteNames = ["Make(Asc)", "AuctionEnd(Asc)", "CreatedAt(Asc)"];

        var collection = db.Collection<ItemDocument>();
        var existingNames = (await collection.Indexes.List().ToListAsync())
            .Select(index => index["name"].AsString)
            .ToHashSet();

        foreach (var obsoleteName in obsoleteNames)
        {
            if (!existingNames.Contains(obsoleteName))
                continue;

            logger.LogInformation(
                "Dropping obsolete index {IndexName} on {Collection}", obsoleteName, "Items");
            await db.Index<ItemDocument>().DropAsync(obsoleteName);
        }
    }
}
