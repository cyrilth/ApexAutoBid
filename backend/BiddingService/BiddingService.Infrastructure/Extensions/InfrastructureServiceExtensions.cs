using BiddingService.Application.Configuration;
using BiddingService.Application.Services;
using BiddingService.Domain.Interfaces;
using BiddingService.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace BiddingService.Infrastructure.Extensions;

/// <summary>
/// Registers all Infrastructure-layer services into the DI container.
/// Call <c>builder.Services.AddInfrastructureServices(builder.Configuration)</c>
/// from the API's <c>Program.cs</c>.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MongoDbConnection is the singleton holder DbInitializer.InitDbAsync populates with
        // the connected DB instance (MongoDB.Entities 25.1.0 has no static default-instance
        // gateway — see MongoDbConnection's XML doc). Registered here so the same instance
        // DbInitializer resolves via GetRequiredService<MongoDbConnection>() is the one
        // BidRepository/AuctionRepository receive.
        services.AddSingleton<MongoDbConnection>();

        services.AddScoped<IBidRepository, BidRepository>();
        services.AddScoped<IAuctionRepository, AuctionRepository>();

        // ── Mongo bus-outbox wiring (Task 4/11) ──────────────────────────────────
        //
        // IMongoClient/IMongoDatabase: MassTransit's Mongo outbox (Program.cs's AddMassTransit)
        // needs driver-level types it can resolve from the container — intentionally SEPARATE
        // from the MongoDB.Entities DB instance DbInitializer creates via DB.InitAsync (see
        // MongoDbConnection's XML doc), even though both point at the exact same server and
        // database (DbInitializer.DatabaseName). Mirrors SearchService's identical pair.
        services.AddSingleton<IMongoClient>(_ =>
        {
            var connectionString = configuration.GetConnectionString("MongoDbConnection")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:MongoDbConnection is not configured");
            return new MongoClient(connectionString);
        });

        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(DbInitializer.DatabaseName));

        // Required by MassTransit.MongoDbIntegration.MongoDbContext.GetCollection<BidDocument>()
        // — live-verified during this task (see BidPlacementUnitOfWork's remarks): the bus
        // outbox resolves IMongoCollection<T> from the container per document type passed to
        // GetCollection<T>(), rather than deriving it from IMongoDatabase itself. Scoped (not
        // singleton) to match the lifetime of the IMongoDatabase-derived collection handle and
        // of the MongoDbContext/transaction it participates in.
        services.AddScoped(sp =>
            sp.GetRequiredService<IMongoDatabase>().GetCollection<BidDocument>("Bids"));

        services.AddScoped<IBidPlacementUnitOfWork, BidPlacementUnitOfWork>();

        // Same requirement as the BidDocument registration above, for
        // AuctionFinalizationUnitOfWork's own MongoDbContext.GetCollection<AuctionDocument>()
        // call (Phase 5 Tasks 11/12).
        services.AddScoped(sp =>
            sp.GetRequiredService<IMongoDatabase>().GetCollection<AuctionDocument>("Auctions"));

        services.AddScoped<IAuctionFinalizationUnitOfWork, AuctionFinalizationUnitOfWork>();

        // Grace-period setting (phase-end code review Critical 2) — bound here so the
        // Application layer (FinalizationOptions/AuctionFinalizationAppService) stays free of
        // any Microsoft.Extensions.Options wiring concerns, mirroring
        // AuctionService.Infrastructure's identical ImagesOptions binding split.
        services.Configure<FinalizationOptions>(configuration.GetSection(FinalizationOptions.SectionName));

        return services;
    }
}
