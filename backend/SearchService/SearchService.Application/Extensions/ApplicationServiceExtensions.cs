using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Application.Services;

namespace SearchService.Application.Extensions;

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
        // ItemMappingConfig) and register the resulting config as a singleton
        // TypeAdapterConfig, then register IMapper → ServiceMapper so consumers can
        // inject IMapper.
        //
        // A fresh TypeAdapterConfig is used rather than the mutable static
        // GlobalSettings singleton so each DI container (notably parallel test hosts)
        // owns an isolated mapping configuration. Mirrors AuctionService.Application's
        // ApplicationServiceExtensions.
        var config = new TypeAdapterConfig();
        config.Scan(Assembly.GetExecutingAssembly());

        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        // TimeProvider.System (Phase 2 Task 5): SearchAppService resolves "now" through this
        // rather than DateTime.UtcNow so the EndingSoon/Finished filter unit tests
        // (Phase 2 Task 9) can substitute a fake clock (FakeTimeProvider) instead of racing
        // the real one.
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<ISearchService, SearchAppService>();

        // Phase 2 Task 6: IDataSyncService orchestrates the HTTP polling fallback.
        // IAuctionServiceClient (its collaborator) is registered in Infrastructure
        // (AddInfrastructureServices, via AddHttpClient<IAuctionServiceClient, ...>) —
        // registration order between the two AddXServices calls in Program.cs doesn't
        // matter; both land in the same IServiceCollection before the container is built.
        services.AddScoped<IDataSyncService, DataSyncService>();

        // MassTransit consumers are registered separately, in the API's Program.cs, via
        // x.AddConsumersFromNamespaceContaining<...>() — not here.

        return services;
    }
}
