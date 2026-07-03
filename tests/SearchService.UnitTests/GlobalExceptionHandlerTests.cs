using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SearchService.API.Handlers;
using Xunit;

namespace SearchService.UnitTests;

/// <summary>
/// Unit tests for <see cref="GlobalExceptionHandler"/> (Phase 2 Task 13): asserts
/// <c>TryHandleAsync</c> writes the correct RFC 7807 ProblemDetails shape via
/// <see cref="IProblemDetailsService"/>, that the response <c>Detail</c> differs between
/// Development (full exception) and Production (generic message, never leaking the exception),
/// and that the resolved traceId is included in the structured log entry (Requirements §13.1).
///
/// AuctionService.API has no dedicated unit test for its (identically-shaped) handler class —
/// only an integration-level content-type assertion (see
/// AuctionService.IntegrationTests/ErrorHandlingTests.cs, which doesn't exercise the 500 path at
/// all). This is therefore new coverage added for SearchService per Phase 2 Task 13's scope,
/// following this project's existing substitute-the-dependency-and-assert xUnit v3 + NSubstitute
/// conventions (see SearchControllerTests).
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static DefaultHttpContext BuildHttpContext() => new() { TraceIdentifier = "test-trace-id" };

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
    public async Task TryHandleAsync_SetsStatus500AndWritesProblemDetailsWithTheGenericTitle()
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
