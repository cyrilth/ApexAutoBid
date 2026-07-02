var builder = WebApplication.CreateBuilder(args);

// ── Controllers ───────────────────────────────────────────────────────────────
//
// Full wiring (MongoDB.Entities, MassTransit, Mapster, OpenAPI/Scalar pipeline,
// authentication) lands in later tasks. This is scaffolding only — enough to
// build and run.

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => Results.Ok("SearchService is running."));

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
