using AuctionService.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace AuctionService.IntegrationTests;

/// <summary>
/// Boots the real Auction Service in-memory against throwaway PostgreSQL and RabbitMQ
/// containers. The app's MassTransit/RabbitMQ config is left untouched (MassTransit throws
/// if AddMassTransit is called twice, so we provide a real broker rather than swap the bus);
/// the RabbitMQ container is pinned to host port 5672 with a non-guest user because the app
/// connects to localhost:5672 and RabbitMQ's built-in guest user is loopback-restricted.
/// Authentication is replaced with <see cref="TestAuthHandler"/>.
/// </summary>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("apex")
        .WithPassword("apex")
        .WithPortBinding(5672, 5672)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbitMq.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["RabbitMq:Host"] = "localhost",
                ["RabbitMq:Username"] = "apex",
                ["RabbitMq:Password"] = "apex",
                ["IdentityServiceUrl"] = "https://localhost:5001",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Make the header-driven test scheme the default so [Authorize] uses it.
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public new async Task DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
