using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SearchService.Infrastructure.Extensions;

/// <summary>
/// Registers all Infrastructure-layer services into the DI container.
/// Call <c>builder.Services.AddInfrastructureServices(builder.Configuration)</c>
/// from the API's <c>Program.cs</c>.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Currently a no-op. Unlike <c>AuctionDbContext</c> (an EF Core <c>DbContext</c> that
    /// needs <c>services.AddDbContext</c>), <c>MongoDB.Entities</c> exposes a static
    /// <c>DB</c> gateway that is connected once via <c>DbInitializer.InitDbAsync</c> from
    /// Program.cs — mirroring where AuctionService.API calls
    /// <c>DbInitializer.InitDbAsync(app.Services)</c> — so there is nothing to register in
    /// the container for the Mongo connection itself. Kept as the composition-root entry
    /// point for Infrastructure-layer DI registrations added by later tasks (e.g. the item
    /// repository, gRPC/HTTP clients).
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services;
    }
}
