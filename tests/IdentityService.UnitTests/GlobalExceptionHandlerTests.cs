using IdentityService.Handlers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for <see cref="GlobalExceptionHandler"/> (Phase 3 Task 17). Mirrors
/// SearchService.UnitTests/GlobalExceptionHandlerTests.cs's shape (Development-vs-Production
/// detail, traceId presence in the log) plus new coverage for this service's own
/// interpretation — the JSON-vs-HTML split (see the handler's own remarks for the full
/// reasoning): a request that explicitly accepts <c>text/html</c> is declined (returns
/// <see langword="false"/>, no ProblemDetails written) so ExceptionHandlerMiddleware falls
/// through to re-executing Duende's own <c>/Error</c> page; everything else (no Accept header,
/// or one that doesn't mention text/html) gets RFC 7807 ProblemDetails, same as
/// AuctionService.API/SearchService.API.
/// <para>
/// No separate integration-level "throw endpoint" test was added — IdentityService has no
/// production controller/minimal-API surface to attach a synthetic throwing endpoint to without
/// adding production code purely for a test (Razor Pages would need a whole new page); this
/// unit-level coverage plus the live smoke-test evidence in this task's report is the disclosed,
/// deliberate scope boundary.
/// </para>
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static DefaultHttpContext BuildHttpContext(string? acceptHeader = null)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "test-trace-id" };
        if (acceptHeader is not null)
        {
            context.Request.Headers.Accept = new StringValues(acceptHeader);
        }

        return context;
    }

    private static (GlobalExceptionHandler Handler, IProblemDetailsService ProblemDetailsService, ILogger<GlobalExceptionHandler> Logger)
        BuildHandler(bool isDevelopment)
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName = isDevelopment ? Environments.Development : Environments.Production;

        var logger = Substitute.For<ILogger<GlobalExceptionHandler>>();

        return (new GlobalExceptionHandler(problemDetailsService, environment, logger), problemDetailsService, logger);
    }

    [Fact]
    public async Task TryHandleAsync_NoAcceptHeader_SetsStatus500AndWritesProblemDetailsWithTheGenericTitle()
    {
        var (handler, problemDetailsService, _) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext();
        var exception = new InvalidOperationException("boom");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        await problemDetailsService.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(ctx =>
            ctx.HttpContext == httpContext &&
            ctx.Exception == exception &&
            ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError &&
            ctx.ProblemDetails.Title == "An unexpected error occurred."));
    }

    [Fact]
    public async Task TryHandleAsync_JsonAcceptHeader_WritesProblemDetails()
    {
        var (handler, problemDetailsService, _) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext("application/json");
        var exception = new InvalidOperationException("boom");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        await problemDetailsService.Received(1).TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    // ── Phase 3 Task 17 — IdentityService-specific JSON-vs-HTML split ────────────
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]
    public async Task TryHandleAsync_BrowserAcceptHeader_DeclinesWithoutWritingProblemDetails(string acceptHeader)
    {
        var (handler, problemDetailsService, _) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext(acceptHeader);
        var exception = new InvalidOperationException("boom");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Declines — ExceptionHandlerMiddleware (decompile-confirmed, see the handler's own
        // remarks) falls through to re-executing "/Error" for a declined request; this handler
        // itself must never have written a JSON body for a browser navigation.
        Assert.False(handled);
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task TryHandleAsync_BrowserAcceptHeader_StillLogsTheFullException()
    {
        // Declining to WRITE a response must not mean declining to LOG — the exception still
        // needs to reach the logs even when the browser gets the HTML fallback instead of
        // ProblemDetails (Requirements §13.1/§13.5: full detail always in logs).
        var (handler, _, logger) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext("text/html");
        var exception = new InvalidOperationException("boom");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var call = Assert.Single(logger.ReceivedCalls());
        var arguments = call.GetArguments();
        Assert.Equal(LogLevel.Error, arguments[0]);
        Assert.Same(exception, arguments[3]);
    }

    [Fact]
    public async Task TryHandleAsync_InDevelopment_DetailContainsTheFullExceptionText()
    {
        var (handler, problemDetailsService, _) = BuildHandler(isDevelopment: true);
        var httpContext = BuildHttpContext();
        var exception = new InvalidOperationException("boom - safe to expose in dev");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        await problemDetailsService.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(ctx =>
            ctx.ProblemDetails.Detail == exception.ToString()));
    }

    [Fact]
    public async Task TryHandleAsync_InProduction_DetailIsGenericAndNeverLeaksTheException()
    {
        var (handler, problemDetailsService, _) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext();
        var exception = new InvalidOperationException("boom - must not leak to prod clients");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        await problemDetailsService.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(ctx =>
            ctx.ProblemDetails.Detail == "An unexpected error occurred." &&
            !ctx.ProblemDetails.Detail!.Contains("boom")));
    }

    [Fact]
    public async Task TryHandleAsync_LogsTheFullExceptionWithTheResolvedTraceId()
    {
        // No Activity is started in this test, so the handler falls back to
        // httpContext.TraceIdentifier — matching the fallback branch it exercises in the real
        // pipeline whenever W3C trace context isn't already flowing (Requirements §13.5).
        var (handler, _, logger) = BuildHandler(isDevelopment: false);
        var httpContext = BuildHttpContext();
        var exception = new InvalidOperationException("boom");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var call = Assert.Single(logger.ReceivedCalls());
        var arguments = call.GetArguments();
        Assert.Equal(LogLevel.Error, arguments[0]);
        Assert.Same(exception, arguments[3]);

        // FormattedLogValues.ToString() (the state argument) renders the message template with
        // its named placeholders substituted — the same rendering the real console/JSON
        // formatters would produce — without needing to reference its internal type by name.
        var message = arguments[2]!.ToString();
        Assert.Contains(httpContext.TraceIdentifier, message);
    }
}
