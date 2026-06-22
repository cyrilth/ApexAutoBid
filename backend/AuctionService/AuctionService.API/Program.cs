using AuctionService.Application.Extensions;
using AuctionService.Infrastructure.Data;
using AuctionService.Infrastructure.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (DbContext + repositories) ───────────────────────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster + IAuctionService) ─────────────────────────

builder.Services.AddApplicationServices();

// ── Messaging (MassTransit + RabbitMQ + EF Core transactional Outbox) ────────
//
// The transactional Outbox is backed by AuctionDbContext so that published events
// and domain writes are committed atomically in the same PostgreSQL transaction.
// QueryDelay controls how frequently the outbox delivery worker polls for unsent
// messages. UseBusOutbox() hooks MassTransit's IPublishEndpoint / ISendEndpoint
// to write to the outbox rather than sending directly to the broker.
//
// KebabCaseEndpointNameFormatter with the "auction" prefix produces queue names
// like "auction-<consumer-name>", keeping queues identifiable and collision-free
// on the shared RabbitMQ broker.

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AuctionDbContext>(o =>
    {
        o.QueryDelay = TimeSpan.FromSeconds(10);
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("auction", false));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.ConfigureEndpoints(context);
    });
});

// ── Controllers ───────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// ── Authentication / Authorization ───────────────────────────────────────────
//
// JWT bearer authentication against Duende IdentityServer (Phase 3).
// NameClaimType is set to "username" so that User.Identity!.Name returns the
// username claim emitted by IdentityServer — this keeps Seller stamping and
// ownership checks consistent throughout the controller.
//
// ValidateAudience is false because the IdentityServer resource configuration
// is added in Phase 3; enabling it now would reject all tokens with no audience.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
    });

builder.Services.AddAuthorization();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

await DbInitializer.InitDbAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
