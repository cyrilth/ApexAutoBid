using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Boots the real Notification Service in-memory against a throwaway RabbitMQ container.
/// Mirrors <c>BiddingService.IntegrationTests.CustomWebAppFactory</c>'s pattern, simplified: this
/// service has no database (Architecture.md's resilience table: "not applicable" for the outbox
/// here), so there is no MongoDb/Postgres Testcontainer to start alongside RabbitMQ.
/// Authentication is replaced with <see cref="TestAuthHandler"/> so tests can "authenticate" a
/// SignalR connection as a given username without a real IdentityServer.
/// </summary>
public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("apex")
        .WithPassword("apex")
        .Build();

    /// <summary>RabbitMQ connection details for tests that need to attach their own bus, though
    /// every test in this suite instead resolves the app's own already-connected <c>IBus</c>
    /// from <see cref="WebApplicationFactory{TEntryPoint}.Services"/> (simpler — the app under
    /// test consumes from this very same broker).</summary>
    public string RabbitMqHost => _rabbitMq.Hostname;

    public ushort RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public const string RabbitMqUsername = "apex";
    public const string RabbitMqPassword = "apex";

    public async ValueTask InitializeAsync()
    {
        await _rabbitMq.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = RabbitMqHost,
                ["RabbitMq:Port"] = RabbitMqPort.ToString(),
                ["RabbitMq:Username"] = RabbitMqUsername,
                ["RabbitMq:Password"] = RabbitMqPassword,
                // Dummy — never actually dialled. Real token validation against IdentityServer
                // is bypassed entirely: ConfigureTestServices below replaces the whole JwtBearer
                // scheme with TestAuthHandler, so Program.cs's AddJwtBearer(options => options
                // .Authority = ...) never gets exercised in this suite. This just needs to be
                // configured (some services throw at startup otherwise); Program.cs itself never
                // fails without it either way.
                ["IdentityServiceUrl"] = "https://localhost:5001",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Make the test scheme the default so anonymous vs. "authenticated as <username>"
            // SignalR connections behave exactly as TestAuthHandler's own remarks describe.
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // xUnit v3's IAsyncLifetime inherits IAsyncDisposable, so teardown is a ValueTask-returning
    // DisposeAsync — the same signature WebApplicationFactory already exposes.
    public override async ValueTask DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Groups every test class in this suite into a single xUnit collection so they share one
/// factory/RabbitMQ container instance instead of each spinning up their own. Collection members
/// run sequentially against the shared instance — tests use unique auction ids/usernames per test
/// to avoid interfering with each other over the shared broker and hub.
/// </summary>
[CollectionDefinition(Name)]
public class NotificationServiceCollection : ICollectionFixture<CustomWebAppFactory>
{
    public const string Name = "NotificationService.API";
}
