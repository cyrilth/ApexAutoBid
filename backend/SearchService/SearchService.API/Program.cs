using MassTransit;
using SearchService.Application.Extensions;
using SearchService.Infrastructure.Data;
using SearchService.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (Phase 2 Task 3: MongoDB connection setup) ───────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster) ───────────────────────────────────────────

builder.Services.AddApplicationServices();

// ── Messaging (MassTransit + RabbitMQ) ───────────────────────────────────────
//
// The five Phase 2 Task 4 consumers (AuctionCreated/Updated/Deleted, BidPlaced,
// AuctionFinished) keep the search index in sync with the Auction/Bidding Services.
// No EF/Mongo transactional outbox-inbox here yet — that lands with the Mongo change-
// stream/outbox work in Phase 2 Task 7.
//
// KebabCaseEndpointNameFormatter with the "search" prefix produces queue names like
// "search-auction-created", mirroring AuctionService.API's "auction-<consumer-name>"
// convention on the same shared RabbitMQ broker.
//
// UseMessageRetry gives every consumer endpoint a modest redelivery policy: AuctionService
// does not configure one today, so this is a deliberate addition for Search's consumers —
// transient failures (e.g. a momentarily unreachable MongoDB) get 5 attempts, 5 seconds
// apart, before the message is moved to its endpoint's _error queue.

builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining<SearchService.Application.Consumers.AuctionCreatedConsumer>();

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("search", false));

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitPort = builder.Configuration.GetValue<ushort?>("RabbitMq:Port") ?? 5672;
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", rabbitPort, "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(5)));

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddControllers();

var app = builder.Build();

await DbInitializer.InitDbAsync(app.Services);

app.MapControllers();

app.MapGet("/", () => Results.Ok("SearchService is running."));

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
