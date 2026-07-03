using IdentityService;
using IdentityService.Data;

var builder = WebApplication.CreateBuilder(args);

var app = builder
    .ConfigureServices()
    .ConfigurePipeline();

// Applies pending EF Core migrations unconditionally on every startup (Phase 3 Task 2)
// — not gated behind /seed, so the apexautobid_identity database and schema exist on a
// plain `dotnet run` too, mirroring AuctionService.API's DbInitializer.InitDbAsync call.
await DbInitializer.InitDbAsync(app.Services);

// this seeding is only for the template to bootstrap the DB and users.
// in production you will likely want a different approach.
if (args.Contains("/seed"))
{
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
    seedLogger.LogInformation("Seeding database");
    SeedData.EnsureSeedData(app);
    seedLogger.LogInformation("Done seeding database, exiting");
    return;
}

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
