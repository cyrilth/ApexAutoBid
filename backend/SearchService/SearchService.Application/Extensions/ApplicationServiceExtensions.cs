using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

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

        // MassTransit consumers are registered separately, in the API's Program.cs, via
        // x.AddConsumersFromNamespaceContaining<...>() — not here.

        return services;
    }
}
