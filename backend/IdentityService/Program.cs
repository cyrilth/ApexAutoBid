using IdentityService;

var builder = WebApplication.CreateBuilder(args);

var app = builder
    .ConfigureServices()
    .ConfigurePipeline();

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
