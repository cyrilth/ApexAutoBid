using AuctionService.Application.Extensions;
using AuctionService.Infrastructure.Data;
using AuctionService.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (DbContext + repositories) ───────────────────────

builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Application services (Mapster + IAuctionService) ─────────────────────────

builder.Services.AddApplicationServices();

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
