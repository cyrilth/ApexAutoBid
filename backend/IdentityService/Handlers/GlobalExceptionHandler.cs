using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Handlers;

/// <summary>
/// Converts genuinely unhandled exceptions into RFC 7807 ProblemDetails (500) responses — but
/// ONLY for the JSON-facing surface of this service (Phase 3 Task 17 / Requirements §13.1).
/// <para>
/// <b>IdentityService-specific interpretation (disclosed, not silently assumed):</b> unlike
/// AuctionService.API/SearchService.API (pure JSON APIs, where this class is mirrored
/// byte-for-byte), IdentityService is a Razor Pages UI (Login/Register/ExternalLogin/etc.) PLUS
/// Duende's own protocol endpoints (<c>/connect/*</c>, <c>/.well-known/*</c>). Task 17's "for the
/// API endpoints" is read as: JSON-facing callers (OIDC/OAuth clients hitting Duende's protocol
/// surface, and any future JSON API this service grows) get RFC 7807 ProblemDetails; a real
/// BROWSER navigating a Razor Page should get an HTML error experience, not a JSON blob it can't
/// render. <see cref="TryHandleAsync"/> therefore returns <see langword="false"/> (declining to
/// handle) for requests that look like a browser navigation — <c>ExceptionHandlerMiddleware</c>
/// (decompile-confirmed, not assumed: it tries every registered <see cref="IExceptionHandler"/>
/// in order and falls through to <c>ExceptionHandlingPath</c> re-execution the first one that
/// returns <see langword="false"/>) then re-executes the request against
/// <c>/Error</c> — Duende's OWN existing, unmodified error page
/// (Pages/Error/Index.cshtml(.cs)) — reused rather than reinvented. That page's
/// <c>_interaction.GetErrorContextAsync(null, ct)</c> call is decompile-confirmed to return
/// <see langword="null"/> gracefully for a missing errorId (not throw), and the page's Razor
/// markup already guards on a null <c>Error</c>, so re-executing it for a generic unhandled
/// exception (no OIDC error context at all) safely renders a plain "Sorry, there was an error"
/// — no changes needed to that page for this task.
/// </para>
/// <para>
/// <b>JSON-vs-HTML decision mechanism</b>: content negotiation, not path matching. A request is
/// treated as "wants HTML" (and this handler steps aside) only if its <c>Accept</c> header
/// explicitly mentions <c>text/html</c> — which every real browser navigation includes, and
/// which OIDC/OAuth client libraries and Duende's own internal requests never do. Deliberately
/// NOT reusing ASP.NET Core's own <c>DefaultProblemDetailsWriter.CanWrite</c> heuristic
/// (decompile-checked): that logic treats a bare <c>*/*</c> Accept entry as "wants JSON", but
/// real browsers conventionally end their Accept header with exactly <c>*/*;q=0.8</c> as a
/// low-priority catch-all — reusing it here would misclassify ordinary browser navigations as
/// JSON-facing. Also deliberately NOT path-matching against a hardcoded list of Duende's known
/// protocol routes (<c>/connect/token</c>, <c>/connect/authorize</c>, <c>/.well-known/...</c>,
/// <c>/connect/userinfo</c>, ...): that list is large, Duende-version-dependent, and would need
/// maintenance as endpoints are added; content negotiation covers the same ground automatically
/// (a machine client hitting any of those paths never sends <c>Accept: text/html</c>) and also
/// automatically covers "any future API endpoints" this service grows, per the task's own
/// wording, without a code change.
/// </para>
/// <para>
/// Duende's own protocol endpoints already convert their OWN internal/expected error conditions
/// (bad client_id, invalid redirect_uri, etc.) into protocol-conformant responses (redirects
/// carrying OAuth error codes, or its own /Error page) entirely within
/// <c>UseIdentityServer()</c>'s middleware — those paths never throw a .NET exception in the
/// first place, so they never reach this handler. This handler is purely the backstop for a
/// genuinely unhandled exception (a bug), and does not participate in normal OIDC/OAuth error
/// semantics at all — nothing about authorize-request error behavior changes.
/// </para>
/// <para>
/// The full exception is always logged via <see cref="ILogger"/> regardless of which branch
/// runs; the HTTP response exposes the exception type/message/stack trace only in Development —
/// in Production it returns a generic message, correlated to the logs by <c>traceId</c>
/// (Requirements §13.1). In THIS service's current wiring, that Development branch is shadowed
/// in practice by <c>UseDeveloperExceptionPage()</c> (registered first, so it catches
/// everything before this handler ever runs in Development — see HostingExtensions.cs's
/// remarks) — kept for exact parity with AuctionService.API/SearchService.API's reviewed
/// pattern, as defense-in-depth against a future reordering, and because it IS exercised
/// directly by this class's own unit tests (which call <see cref="TryHandleAsync"/> without
/// going through the ASP.NET Core pipeline at all).
/// </para>
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // Logged unconditionally, before the JSON-vs-HTML branch — the log entry (and its
        // traceId) exists regardless of which response shape the caller ends up getting.
        logger.LogError(exception,
            "Unhandled exception handling {Method} {Path} (traceId {TraceId})",
            httpContext.Request.Method, httpContext.Request.Path, traceId);

        if (!IsJsonFacingRequest(httpContext))
        {
            // Decline — ExceptionHandlerMiddleware falls through to re-executing /Error
            // (see this class's remarks). Response.StatusCode (500) is already set by the
            // middleware before any handler runs, so the re-executed HTML page still reports
            // 500, just with a human-readable body instead of a JSON one.
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred.",
            },
        });
    }

    private static bool IsJsonFacingRequest(HttpContext httpContext)
    {
        var acceptsHtml = httpContext.Request.Headers.Accept
            .Any(value => value is not null && value.Contains("text/html", StringComparison.OrdinalIgnoreCase));

        return !acceptsHtml;
    }
}
