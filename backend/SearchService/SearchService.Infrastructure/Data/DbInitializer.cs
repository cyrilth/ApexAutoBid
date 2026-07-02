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
    /// Search Service owns the <c>search</c> MongoDB database.
    /// </summary>
    private const string DatabaseName = "search";

    public static async Task InitDbAsync(IServiceProvider services)
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

        // NOTE: connecting to a not-yet-ready MongoDB (common when the container starts
        // before the service does) currently throws and exits. A startup retry policy is
        // added with the Docker work in Phase 2 Task 8 (Dockerize the Search Service),
        // where service start-ordering actually matters. (Task 6's Polly retry is for the
        // HTTP polling fallback to the Auction Service — a different concern.)
        logger.LogInformation("Connecting to MongoDB database {DatabaseName}", DatabaseName);
        var db = await DB.InitAsync(DatabaseName, MongoClientSettings.FromConnectionString(connectionString));
        logger.LogInformation("MongoDB connection initialized for database {DatabaseName}", DatabaseName);

        // MongoDB.Entities 25.1.0 exposes Find/Save/Delete/Update/Index as instance members
        // on the connected DB instance, not static passthroughs — hand it to the singleton
        // holder so ItemRepository (and anything else added later) can reach it without a
        // service locator. See MongoDbContext's XML doc for the full rationale.
        scope.ServiceProvider.GetRequiredService<MongoDbContext>().SetInstance(db);

        await EnsureIndexesAsync(db, logger);
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
