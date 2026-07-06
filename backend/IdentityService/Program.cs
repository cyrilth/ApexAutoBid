using IdentityService;
using IdentityService.Data;

var builder = WebApplication.CreateBuilder(args);

var app = builder
    .ConfigureServices()
    .ConfigurePipeline();

// Applies pending EF Core migrations unconditionally on every startup (Phase 3 Task 2)
// — not gated behind /seed, so the apexautobid_identity database and schema exist on a
// plain `dotnet run` too, mirroring AuctionService.API's DbInitializer.InitDbAsync call.
// Retries a bounded ~27s window via Polly if PostgreSQL isn't accepting connections yet
// (Phase 3 Task 6) — see DbInitializer's XML remarks for the retry-predicate and fail-hard
// rationale. app.Lifetime.ApplicationStopping is threaded through as defense-in-depth
// cancellation, same caveat as SearchService.API's identical pre-app.Run() wiring (its
// DbInitializer.ConnectWithRetryAsync XML remarks explain why pre-Run() signal delivery is
// best-effort only).
await DbInitializer.InitDbAsync(app.Services, app.Lifetime.ApplicationStopping);

// Seeds the Requirements.md §8.1 dev/demo users (bob, alice, tom, admin) on every startup
// (Phase 3 Task 5) — on-startup rather than the template's original `/seed`-gated, one-shot
// invocation (Requirements.md §8: "Each service seeds its own store on startup"; a manual
// `/seed` arg is also awkward once this service runs as a container (Task 9) — there's no
// interactive step to append extra CLI args to a Dockerfile ENTRYPOINT). EnsureSeedDataAsync
// is idempotent (find-by-username, create-if-absent; role-assignment only if missing).
//
// Development-only, unlike the sibling services' unconditional seeding: §8's sample data is
// "for development and demos", and what this seeds is not demo *data* but working ACCOUNTS
// with the publicly-committed shared password — including one holding the `admin` role. An
// environment mix-up that seeded fake auctions into production would be cosmetic; one that
// created admin/<committed password> in production would be a standing credential backdoor.
if (app.Environment.IsDevelopment())
{
    await SeedData.EnsureSeedDataAsync(app.Services);
}

app.Run();

// Exposes the implicit Program class (top-level statements) to the integration test
// project so WebApplicationFactory<Program> can bootstrap the real app in-memory.
public partial class Program { }
