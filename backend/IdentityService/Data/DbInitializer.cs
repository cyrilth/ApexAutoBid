using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly;
using Polly.Retry;

namespace IdentityService.Data;

/// <summary>
/// Applies pending EF Core migrations on application startup, retrying the connection with a
/// bounded Polly window if PostgreSQL isn't ready yet (Phase 3 Task 6).
/// Call <c>await DbInitializer.InitDbAsync(app.Services)</c> from <c>Program.cs</c>
/// immediately after building the app, unconditionally (not only under <c>/seed</c>) —
/// so the <c>apexautobid_identity</c> database and schema exist on first run even when
/// the process is never started with <c>/seed</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-hard once the window is exhausted:</b> the pipeline lets the final attempt's
/// exception propagate uncaught, crashing startup — matching
/// <c>SearchService.Infrastructure.Data.DbInitializer.ConnectWithRetryAsync</c>'s rationale.
/// PostgreSQL is this service's own datastore (every login/registration/token issuance reads
/// or writes it), so there is no meaningful "degraded but running" state without it — retrying
/// forever would just hide a genuine configuration/infrastructure problem instead of surfacing
/// it, and container orchestrators are built to restart a process that exits non-zero.
/// </para>
/// <para>
/// <b>Cancellation:</b> <see cref="InitDbAsync"/> is called from <c>Program.cs</c> before
/// <c>app.Run()</c>, and the generic host's console lifetime doesn't register its Ctrl+C/
/// SIGTERM handlers until <c>Run()</c>/<c>StartAsync()</c> actually starts — so a caller-supplied
/// <paramref name="cancellationToken"/> reaching this method pre-<c>Run()</c> is best-effort at
/// best today, same caveat as the Search Service's initializer.
/// </para>
/// </remarks>
public static class DbInitializer
{
    /// <summary>
    /// Bounded startup retry window (Phase 3 Task 6): 10 attempts, 3 seconds apart (~27s
    /// total) — the same shape as SearchService.Infrastructure.Data.DbInitializer's Mongo
    /// connect retry, covering the common case of this service's container starting before
    /// the postgres container is actually ready to accept connections.
    /// </summary>
    private const int MaxAttempts = 10;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public static async Task InitDbAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        // ── Retry predicate ───────────────────────────────────────────────────────────────
        //
        // Verified against Npgsql 10.0.3's actual behavior via ilspycmd decompilation, not
        // assumed:
        //
        // NpgsqlException.IsTransient (the base type — thrown when the connection itself
        // can't be established, e.g. the server isn't accepting connections yet) returns true
        // only when its InnerException is an IOException, SocketException, TimeoutException,
        // or another transient NpgsqlException — exactly "Postgres not ready yet" territory.
        //
        // PostgresException (the subclass raised for actual server-side errors once a TCP
        // connection succeeds — bad migration SQL, constraint violations, etc.) OVERRIDES
        // IsTransient independently, returning true ONLY for a fixed whitelist of SqlState
        // codes that are themselves all connection/availability-class server errors: 53xxx
        // (insufficient_resources), 57P03 (cannot_connect_now), 58000/58030 (system/io
        // errors), 40001/40P01 (serialization_failure/deadlock_detected), 55P03/55006/55000
        // (object not available), 08xxx (connection_exception family), 57P01/57P02/57P05
        // (admin/crash shutdown, idle timeout). A genuine bad-SQL migration failure (e.g.
        // 42601 syntax_error, 23505 unique_violation) is NOT on that list, so IsTransient
        // correctly returns false for it — it is NOT retried and propagates on the very first
        // attempt, rather than delaying crash-loop feedback for ~27s on a non-transient error.
        //
        // Because IsTransient is virtual and PostgresException's override does not chain to
        // the base implementation, handling on NpgsqlException and evaluating ex.IsTransient
        // polymorphically dispatches to whichever of the two behaviors above is correct for
        // the actual exception instance at runtime — one predicate correctly covers both.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(ex => ex.IsTransient),
                MaxRetryAttempts = MaxAttempts - 1, // Polly counts retries IN ADDITION to the original call
                BackoffType = DelayBackoffType.Constant,
                Delay = RetryDelay,
                OnRetry = args =>
                {
                    // AttemptNumber is zero-based; +1 to report "1/10" like SearchService's
                    // ConnectWithRetryAsync does. The final (10th) attempt's exception is
                    // never handed to OnRetry — once MaxRetryAttempts is exhausted it
                    // propagates straight out of ExecuteAsync uncaught (see the fail-hard
                    // remarks on this class).
                    logger.LogWarning(args.Outcome.Exception,
                        "Database migration attempt {Attempt}/{MaxAttempts} failed — retrying in {Delay}",
                        args.AttemptNumber + 1, MaxAttempts, RetryDelay);
                    return default;
                }
            })
            .Build();

        logger.LogInformation("Applying database migrations");

        await pipeline.ExecuteAsync(
            async ct => await context.Database.MigrateAsync(ct),
            cancellationToken);
    }
}
