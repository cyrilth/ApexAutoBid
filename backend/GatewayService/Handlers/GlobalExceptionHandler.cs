using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GatewayService.Handlers;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails (500) responses. The full
/// exception is always logged via <see cref="ILogger"/>; the HTTP response exposes the
/// exception type, message and stack trace only in Development — in Production it returns a
/// generic message, correlated to the logs by <c>traceId</c> (Requirements §13.1).
/// <para>
/// This is the backstop for genuinely unhandled exceptions raised by the gateway's own code
/// (authentication/authorization middleware, the version/health endpoints). It is registered
/// first in the pipeline (see Program.cs) so it wraps everything else — including
/// <c>MapReverseProxy()</c> — but a normal proxied response or error from a downstream service
/// is never an unhandled .NET exception here, so this handler never touches it; YARP's
/// forwarder streams those responses back unchanged.
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

        logger.LogError(exception,
            "Unhandled exception handling {Method} {Path} (traceId {TraceId})",
            httpContext.Request.Method, httpContext.Request.Path, traceId);

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
}
