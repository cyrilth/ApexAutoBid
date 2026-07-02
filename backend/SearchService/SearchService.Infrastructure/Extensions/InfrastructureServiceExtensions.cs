using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Domain.Interfaces;
using SearchService.Infrastructure.Data;

namespace SearchService.Infrastructure.Extensions;

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
        // MongoDbContext is the singleton holder DbInitializer.InitDbAsync populates with
        // the connected DB instance (MongoDB.Entities 25.1.0 has no static default-instance
        // gateway — see MongoDbContext's XML doc). Registered here so the same instance
        // DbInitializer resolves via GetRequiredService<MongoDbContext>() is the one
        // ItemRepository receives.
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IItemRepository, ItemRepository>();

        return services;
    }
}
