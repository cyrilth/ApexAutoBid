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
/// the RabbitMQ container is given a non-guest user because RabbitMQ's built-in guest user is
/// loopback-restricted. Testcontainers assigns a random host port for the container's 5672,
/// and that mapped port is fed into the app via RabbitMq:Port (Program.cs now honors a
/// configurable port), which also means concurrent test runs on the same machine can't
/// collide on a fixed port. Authentication is replaced with <see cref="TestAuthHandler"/>.
/// </summary>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("apex")
        .WithPassword("apex")
        .Build();

    async Task IAsyncLifetime.InitializeAsync()
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
                ["RabbitMq:Host"] = _rabbitMq.Hostname,
                ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
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

    // Explicit IAsyncLifetime implementation rather than `new`-hiding the base method:
    // WebApplicationFactory already exposes a public ValueTask DisposeAsync() (IAsyncDisposable),
    // so declaring `public new async Task DisposeAsync()` hid it, making which teardown ran depend
    // on how xUnit disposed the fixture. As an explicit interface member, xUnit's IAsyncLifetime
    // teardown invokes this deterministically; we dispose the containers and then await
    // base.DisposeAsync() so the in-memory host is torn down too (base dispose is idempotent).
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Groups every test class that depends on <see cref="CustomWebAppFactory"/> into a single
/// xUnit collection so they share one factory instance instead of each spinning up their own
/// PostgreSQL/RabbitMQ containers. xUnit runs distinct test classes in parallel by default;
/// sharing one factory avoids the cost of starting duplicate containers per test class. The
/// RabbitMQ container now binds a random host port (see <see cref="CustomWebAppFactory"/>), so
/// this collection is no longer strictly required to avoid a port collision — it remains for
/// container-reuse efficiency. Collection members still run sequentially against the shared
/// instance.
/// </summary>
[CollectionDefinition(Name)]
public class AuctionServiceApiCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "AuctionService.API";
}
