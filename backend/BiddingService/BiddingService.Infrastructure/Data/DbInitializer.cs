using System.Diagnostics;
using BiddingService.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Entities;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// Handles MongoDB connection initialization and index creation on application startup.
/// Call <c>await DbInitializer.InitDbAsync(app.Services)</c> from the API's
/// <c>Program.cs</c> immediately after <c>var app = builder.Build();</c>. Mirrors
/// <c>SearchService.Infrastructure.Data.DbInitializer</c> — see that type for the full
/// rationale behind each design choice below (retry window, ServerSelectionTimeout,
/// directConnection, etc.), which is identical here.
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// Database name per the database-per-service table (<c>Docs/Architecture.md</c> §4.1) —
    /// Bidding Service owns the <c>bids</c> MongoDB database.
    /// </summary>
    internal const string DatabaseName = "bids";

    private const int MaxConnectAttempts = 10;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(3);

    public static async Task InitDbAsync(
        IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializer));

        var connectionString = config.GetConnectionString("MongoDbConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:MongoDbConnection is not configured");

        var db = await ConnectWithRetryAsync(connectionString, logger, cancellationToken);

        scope.ServiceProvider.GetRequiredService<MongoDbConnection>().SetInstance(db);

        await EnsureIndexesAsync(db, logger);

        await SeedDataAsync(db, logger, cancellationToken);
    }

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
                logger.LogWarning(ex,
                    "MongoDB connection attempt {Attempt}/{MaxAttempts} failed — retrying in {Delay}",
                    attempt, MaxConnectAttempts, ConnectRetryDelay);
                await Task.Delay(ConnectRetryDelay, cancellationToken);
            }
        }

        throw new UnreachableException(
            $"{nameof(ConnectWithRetryAsync)} loop completed without returning or throwing.");
    }

    private static async Task EnsureIndexesAsync(DB db, ILogger logger)
    {
        logger.LogInformation("Ensuring indexes on {Collection}", "Bids");

        // Serves GetByAuctionIdAsync's equality filter on its own, and is a prefix of both
        // compound indexes below.
        await db.Index<BidDocument>().Key(x => x.AuctionId, KeyType.Ascending).CreateAsync();

        // Serves GetHighestAcceptedAmountAsync's exact query shape: filter by AuctionId +
        // BidStatus, sort by Amount descending, limit 1.
        await db.Index<BidDocument>()
            .Key(x => x.AuctionId, KeyType.Ascending)
            .Key(x => x.BidStatus, KeyType.Ascending)
            .Key(x => x.Amount, KeyType.Descending)
            .CreateAsync();

        // Serves GetByAuctionIdAsync's exact sort shape: filter by AuctionId, sort by BidTime
        // descending (Id ascending tiebreaker is a single-document resolver, not indexed
        // separately — mirrors SearchService's compound-index-plus-Id-tiebreak convention).
        await db.Index<BidDocument>()
            .Key(x => x.AuctionId, KeyType.Ascending)
            .Key(x => x.BidTime, KeyType.Descending)
            .CreateAsync();

        logger.LogInformation("Indexes ready on {Collection}", "Bids");

        // Serves the background finalizer's (Phase 5 Task 12) exact query shape:
        // GetExpiredUnfinalizedAsync filters !Finished && AuctionEnd <= asOf every tick.
        await db.Index<AuctionDocument>()
            .Key(x => x.Finished, KeyType.Ascending)
            .Key(x => x.AuctionEnd, KeyType.Ascending)
            .CreateAsync();

        // Beyond that, the Auctions collection needs no extra index — every other read
        // (AuctionRepository.GetByIdAsync, LocalAuctionProvider) is a single-document lookup
        // by the default _id index.
    }

    /// <summary>
    /// Phase 5 Task 20 — seeds this service's local <c>Auctions</c> projection and its
    /// <c>Bids</c> history, mirroring Requirements.md §8.2 (Auction Service seed) / §8.3
    /// (Bidding Service seed) exactly. Idempotent: skips entirely if any local auction already
    /// exists (mirrors <c>AuctionService.Infrastructure.Data.DbInitializer.SeedDataAsync</c>'s
    /// identical "skip if any row/document exists" convention).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Same auction ids as the Auction Service (Requirements §8.2/§8.3):</b> the ten Guids
    /// below are the exact, fixed, literal values <c>AuctionService.Infrastructure.Data.DbInitializer</c>
    /// now also assigns explicitly to its own ten seed auctions (that file's own seed list was
    /// amended in lockstep with this one — see its remarks). Before that companion change, the
    /// Auction Service left <c>Auction.Id</c> to be generated (a random, per-deployment
    /// UUIDv7), which made "the same id" an unsatisfiable requirement across two independently
    /// seeded datastores; fixing both services to the same well-known literals is what actually
    /// makes a bidder's <c>GET api/auctions/{id}</c> lookup and their
    /// <c>GET api/bids/{auctionId}</c> lookup agree on which auction they mean, in ANY fresh
    /// deployment — not just the one dev database this literal value happened to be copied
    /// from. (Copying the value doesn't change the ALREADY-seeded live Auction Service
    /// database this task ran against — seeding is skip-if-exists there too — only future
    /// from-empty deployments of both services.)
    /// </para>
    /// <para>
    /// <b><c>Finished</c> at seed time:</b> sourced from whether the Auction Service's OWN
    /// seeded <c>Status</c> (§8.2) is <c>Live</c> or not. Auctions #4 (Mercedes SLK,
    /// <c>ReserveNotMet</c>) and #10 (Ford Model T, <c>Finished</c>/sold) are seeded
    /// <c>Finished = true</c> here specifically so the background finalizer (Task 12) never
    /// selects them on its very first tick and re-publishes an <c>AuctionFinished</c> the
    /// Auction/Search Services would otherwise have to reprocess — their outcome was already
    /// established directly in the Auction Service's own seed data, not via that event. Auction
    /// #9 (Audi TT, <c>+6 hours</c>) is deliberately left <c>Finished = false</c> even though
    /// its short offset means it may ALSO already be past <c>AuctionEnd</c> by the time this
    /// runs: unlike #4/#10, the Auction Service's own seed leaves it <c>Live</c> — a genuinely
    /// expired-but-not-yet-finalized auction is exactly the real-world case the finalizer
    /// exists to pick up, and doing so here (once, correctly, with no seed bids so it resolves
    /// unsold) is intended behaviour, not a bug.
    /// </para>
    /// </remarks>
    private static async Task SeedDataAsync(DB db, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.CountAsync<AuctionDocument>(cancellationToken) > 0)
        {
            logger.LogInformation("Bidding seed data already present — skipping");
            return;
        }

        var now = DateTime.UtcNow;

        // Fixed ids — see this method's remarks. Named to match Requirements §8.2's car names.
        var fordGtId = Guid.Parse("019f34ea-a5dc-756c-923d-a43f3e66c6af");
        var bugattiVeyronId = Guid.Parse("019f34ea-a60a-7abc-8c64-8820ce20cd96");
        var fordMustangId = Guid.Parse("019f34ea-a60a-7457-b003-cd061d1a886c");
        var mercedesSlkId = Guid.Parse("019f34ea-a60a-75c8-99df-36981b246048");
        var bmwX1Id = Guid.Parse("019f34ea-a60a-7142-a876-f92dbd679148");
        var ferrariSpiderId = Guid.Parse("019f34ea-a60a-75a4-b99b-568e6f3f0395");
        var ferrariF430Id = Guid.Parse("019f34ea-a60a-7e7c-863d-b313a76f80ec");
        var audiR8Id = Guid.Parse("019f34ea-a60a-70fd-b40f-e6d7c2090457");
        var audiTtId = Guid.Parse("019f34ea-a60a-7ff7-b8c6-27b047c737ca");
        var fordModelTId = Guid.Parse("019f34ea-a60a-73f7-8724-6710dc1f942a");

        // CurrentHigh (phase-end code review Critical 1) is seeded to match each auction's own
        // seed bids below exactly — the highest Accepted/AcceptedBelowReserve amount, or 0 for
        // an auction with no seed bids — so the atomic-accept claim in BidPlacementUnitOfWork
        // starts from a value consistent with this seed data's own bid history, not 0
        // regardless of it.
        var auctions = new List<AuctionDocument>
        {
            // 1 – Ford GT — has seed bids below (current high bid ends up $18,000)
            new() { Id = fordGtId, Seller = "bob", ReservePrice = 20000, AuctionEnd = now.AddDays(10), Finished = false, CurrentHigh = 18000 },
            // 2 – Bugatti Veyron
            new() { Id = bugattiVeyronId, Seller = "alice", ReservePrice = 90000, AuctionEnd = now.AddDays(60), Finished = false, CurrentHigh = 0 },
            // 3 – Ford Mustang (no reserve)
            new() { Id = fordMustangId, Seller = "bob", ReservePrice = 0, AuctionEnd = now.AddDays(4), Finished = false, CurrentHigh = 0 },
            // 4 – Mercedes SLK — Auction Service seed Status = ReserveNotMet (already ended, unsold)
            new() { Id = mercedesSlkId, Seller = "tom", ReservePrice = 50000, AuctionEnd = now.AddDays(-1), Finished = true, CurrentHigh = 45000 },
            // 5 – BMW X1
            new() { Id = bmwX1Id, Seller = "alice", ReservePrice = 20000, AuctionEnd = now.AddDays(20), Finished = false, CurrentHigh = 0 },
            // 6 – Ferrari Spider
            new() { Id = ferrariSpiderId, Seller = "bob", ReservePrice = 20000, AuctionEnd = now.AddDays(45), Finished = false, CurrentHigh = 0 },
            // 7 – Ferrari F-430
            new() { Id = ferrariF430Id, Seller = "alice", ReservePrice = 150000, AuctionEnd = now.AddDays(13), Finished = false, CurrentHigh = 0 },
            // 8 – Audi R8 (no reserve)
            new() { Id = audiR8Id, Seller = "bob", ReservePrice = 0, AuctionEnd = now.AddDays(30), Finished = false, CurrentHigh = 0 },
            // 9 – Audi TT (ending in 6 hours) — Auction Service seed Status = Live; see remarks
            new() { Id = audiTtId, Seller = "tom", ReservePrice = 20000, AuctionEnd = now.AddHours(6), Finished = false, CurrentHigh = 0 },
            // 10 – Ford Model T — Auction Service seed Status = Finished (sold to alice, $25,000)
            new() { Id = fordModelTId, Seller = "bob", ReservePrice = 20000, AuctionEnd = now.AddDays(-2), Finished = true, CurrentHigh = 25000 },
        };

        // Requirements §8.3's bid history table, verbatim.
        var bids = new List<BidDocument>
        {
            // #1 Ford GT — reserve $20,000
            new()
            {
                Id = Guid.NewGuid(), AuctionId = fordGtId, Bidder = "alice",
                BidderEmail = "alice@apexautobid.local", Amount = 15000,
                BidStatus = nameof(BidStatus.AcceptedBelowReserve), BidTime = now.AddMinutes(-30)
            },
            new()
            {
                Id = Guid.NewGuid(), AuctionId = fordGtId, Bidder = "tom",
                BidderEmail = "tom@apexautobid.local", Amount = 18000,
                BidStatus = nameof(BidStatus.AcceptedBelowReserve), BidTime = now.AddMinutes(-20)
            },
            // #4 Mercedes SLK — reserve $50,000; ends $45,000 high, still below reserve
            new()
            {
                Id = Guid.NewGuid(), AuctionId = mercedesSlkId, Bidder = "bob",
                BidderEmail = "bob@apexautobid.local", Amount = 40000,
                BidStatus = nameof(BidStatus.AcceptedBelowReserve), BidTime = now.AddDays(-2)
            },
            new()
            {
                Id = Guid.NewGuid(), AuctionId = mercedesSlkId, Bidder = "alice",
                BidderEmail = "alice@apexautobid.local", Amount = 45000,
                BidStatus = nameof(BidStatus.AcceptedBelowReserve), BidTime = now.AddDays(-2).AddMinutes(10)
            },
            // #10 Ford Model T — reserve $20,000; sold to alice at $25,000
            new()
            {
                Id = Guid.NewGuid(), AuctionId = fordModelTId, Bidder = "tom",
                BidderEmail = "tom@apexautobid.local", Amount = 22000,
                BidStatus = nameof(BidStatus.Accepted), BidTime = now.AddDays(-3)
            },
            new()
            {
                Id = Guid.NewGuid(), AuctionId = fordModelTId, Bidder = "alice",
                BidderEmail = "alice@apexautobid.local", Amount = 25000,
                BidStatus = nameof(BidStatus.Accepted), BidTime = now.AddDays(-3).AddMinutes(10)
            },
        };

        try
        {
            await db.SaveAsync(auctions, cancellationToken);
            await db.SaveAsync(bids, cancellationToken);
            logger.LogInformation(
                "Seeded {AuctionCount} local auction record(s) and {BidCount} bid(s)",
                auctions.Count, bids.Count);
        }
        catch (MongoException ex)
        {
            // Another instance starting concurrently (rolling deploy / scale-up) may have
            // inserted the seed documents between the CountAsync check above and these saves —
            // treat that as already-seeded rather than crashing the process, mirroring
            // AuctionService.Infrastructure.Data.DbInitializer.SeedDataAsync's identical
            // DbUpdateException handling for the same race.
            logger.LogWarning(ex,
                "Bidding seed insert failed — assuming another instance seeded concurrently");
        }
    }
}
