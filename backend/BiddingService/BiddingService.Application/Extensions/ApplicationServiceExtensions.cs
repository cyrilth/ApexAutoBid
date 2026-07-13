using System.Reflection;
using BiddingService.Application.Services;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace BiddingService.Application.Extensions;

/// <summary>
/// Registers all Application-layer services into the DI container.
/// Call <c>builder.Services.AddApplicationServices()</c> from the API's
/// <c>Program.cs</c> — this is the only Mapster entry-point the API needs;
/// it never has to reference Mapster packages directly.
/// </summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Discover and apply every IRegister implementation in this assembly (e.g.
        // BidMappingConfig) and register the resulting config as a singleton
        // TypeAdapterConfig, then register IMapper → ServiceMapper so controllers/services
        // can inject IMapper.
        //
        // A fresh TypeAdapterConfig is used rather than the mutable static
        // GlobalSettings singleton so each DI container (notably parallel test hosts)
        // owns an isolated mapping configuration. Mirrors AuctionService.Application's/
        // SearchService.Application's identical ApplicationServiceExtensions.
        var config = new TypeAdapterConfig();
        config.Scan(Assembly.GetExecutingAssembly());

        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        // TimeProvider.System: BidAppService resolves "now" through this rather than
        // DateTime.UtcNow so the auction-ended/Finished-status unit tests (Task 15.4) can
        // substitute a fake clock (FakeTimeProvider) instead of racing the real one — mirrors
        // SearchService.Application's identical registration.
        services.AddSingleton(TimeProvider.System);

        // Default IAuctionProvider registration — see IAuctionProvider's remarks for how the
        // later gRPC-fallback run overrides this in Program.cs without touching this call.
        services.AddScoped<IAuctionProvider, LocalAuctionProvider>();

        services.AddScoped<IBidService, BidAppService>();

        // Admin bid-moderation endpoints (Phase 11 Task 5.1/5.4 — DELETE api/admin/bids/{id},
        // GET api/admin/bids/stats). IBidRemovalUnitOfWork is registered in Infrastructure's
        // InfrastructureServiceExtensions, mirroring IBidPlacementUnitOfWork's identical split.
        services.AddScoped<IAdminBidService, AdminBidAppService>();

        // Background auction finalizer (Phase 5 Tasks 11/12) — the API-layer hosted service
        // (AuctionFinalizerHostedService) resolves this per tick; see
        // IAuctionFinalizationService's own remarks.
        services.AddScoped<IAuctionFinalizationService, AuctionFinalizationAppService>();

        // Warning 4 (stuck-auction visibility) — singleton, NOT scoped like the finalization
        // service itself above: the consecutive-failure count must survive across ticks, but a
        // fresh AuctionFinalizationAppService is resolved every tick (see
        // IFinalizationFailureTracker's own remarks for why that rules out an instance field).
        services.AddSingleton<IFinalizationFailureTracker, FinalizationFailureTracker>();

        // MassTransit consumers are registered separately, in the API's Program.cs, via
        // x.AddConsumersFromNamespaceContaining<...>() — not here.
        //
        // IBidPlacementUnitOfWork / IAuctionFinalizationUnitOfWork are registered in
        // Infrastructure's InfrastructureServiceExtensions — their implementations depend on
        // MassTransit.MongoDb (an Infrastructure-only package), not on anything in this
        // project.

        return services;
    }
}
