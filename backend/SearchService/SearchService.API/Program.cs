using SearchService.Infrastructure.Data;
using SearchService.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure services (Phase 2 Task 3: MongoDB connection setup) ───────
//
// Full wiring (MassTransit, Mapster, OpenAPI/Scalar pipeline, authentication)
// lands in later tasks. This is scaffolding only — enough to build and run.

builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddControllers();

var app = builder.Build();

await DbInitializer.InitDbAsync(app.Services);

app.MapControllers();

app.MapGet("/", () => Results.Ok("SearchService is running."));

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
