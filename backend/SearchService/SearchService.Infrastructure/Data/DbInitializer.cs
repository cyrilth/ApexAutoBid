using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        await EnsureIndexesAsync(db, logger);
    }

    private static async Task EnsureIndexesAsync(DB db, ILogger logger)
    {
        logger.LogInformation(
            "Ensuring text index on {Collection} ({Fields})", "Items", "Make, Model, Color");

        await db.Index<ItemDocument>()
            .Key(x => x.Make, KeyType.Text)
            .Key(x => x.Model, KeyType.Text)
            .Key(x => x.Color, KeyType.Text)
            .CreateAsync();

        logger.LogInformation("Text index ready on {Collection}", "Items");
    }
}
