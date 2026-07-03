using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

/// <summary>
/// Applies pending EF Core migrations on application startup.
/// Call <c>await DbInitializer.InitDbAsync(app.Services)</c> from <c>Program.cs</c>
/// immediately after building the app, unconditionally (not only under <c>/seed</c>) —
/// so the <c>apexautobid_identity</c> database and schema exist on first run even when
/// the process is never started with <c>/seed</c>.
/// </summary>
public static class DbInitializer
{
    public static async Task InitDbAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        // NOTE: applying migrations against a not-yet-ready database (e.g. the container
        // starting before Postgres) currently throws and exits. No startup retry is wired
        // up here — that is Phase 3 Task 6 (Polly), a deliberately separate task.
        logger.LogInformation("Applying database migrations");
        await context.Database.MigrateAsync();
    }
}
