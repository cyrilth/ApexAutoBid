using System.Reflection;
using AuctionService.Application.Services;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Application.Extensions;

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
        // Discover and apply every IRegister implementation in this assembly
        // (e.g. AuctionMappingConfig) and register the resulting config as a
        // singleton TypeAdapterConfig, then register IMapper → ServiceMapper so
        // controllers / services can inject IMapper.
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(Assembly.GetExecutingAssembly());

        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        services.AddScoped<IAuctionService, AuctionAppService>();

        return services;
    }
}
