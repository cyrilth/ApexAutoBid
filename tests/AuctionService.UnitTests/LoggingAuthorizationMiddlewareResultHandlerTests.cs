using System.Security.Claims;
using AuctionService.API.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="LoggingAuthorizationMiddlewareResultHandler"/> (Phase 3 Task 19
/// follow-up): a Forbidden policy result logs a Warning (username + path, never the email claim
/// — Requirements §13.5) and still delegates the actual response to the real default handler; a
/// Succeeded result logs nothing and still calls through to the pipeline's `next` delegate.
/// <para>
/// <see cref="IAuthenticationService"/> is substituted and wired into a real
/// <see cref="ServiceProvider"/> on <see cref="HttpContext.RequestServices"/> — decompile-
/// confirmed (<c>AuthenticationHttpContextExtensions.GetAuthenticationService</c>) that
/// <c>HttpContext.ForbidAsync()</c> resolves it via
/// <c>context.RequestServices.GetService&lt;IAuthenticationService&gt;()</c>, which the default
/// <see cref="AuthorizationMiddlewareResultHandler"/> this class delegates to calls internally
/// for a Forbidden result — without it, delegating to the real default handler would throw.
/// </para>
/// </summary>
public class LoggingAuthorizationMiddlewareResultHandlerTests
{
    private static (HttpContext Context, IAuthenticationService AuthenticationService) BuildHttpContext(
        string username = "seller-bob")
    {
        var authenticationService = Substitute.For<IAuthenticationService>();
        authenticationService
            .ForbidAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>())
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(authenticationService)
            .BuildServiceProvider();

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], authenticationType: "TestAuth");
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity),
        };
        context.Request.Method = "POST";
        context.Request.Path = "/api/auctions";

        return (context, authenticationService);
    }

    [Fact]
    public async Task HandleAsync_WhenForbidden_LogsWarningAndDelegatesToDefaultHandlerForbid()
    {
        var logger = Substitute.For<ILogger<LoggingAuthorizationMiddlewareResultHandler>>();
        var handler = new LoggingAuthorizationMiddlewareResultHandler(logger);
        var (context, authenticationService) = BuildHttpContext("seller-bob");
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var nextCalled = false;
        Task Next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        await handler.HandleAsync(Next, context, policy, PolicyAuthorizationResult.Forbid());

        // Logged exactly once, at Warning, with the username and path — never the email claim
        // value (there isn't one in this test's principal at all, which is itself part of the
        // point: this handler never reads email/claim VALUES, only Identity.Name and the path).
        var call = Assert.Single(logger.ReceivedCalls());
        var arguments = call.GetArguments();
        Assert.Equal(LogLevel.Warning, arguments[0]);
        var message = arguments[2]!.ToString();
        Assert.Contains("seller-bob", message);
        Assert.Contains("/api/auctions", message);

        // Delegates the actual response to the real default handler — `next` is only ever
        // called for a Succeeded result, never Forbidden — and the default handler's own
        // Forbidden branch calls IAuthenticationService.ForbidAsync (verified as a real call,
        // not just "didn't throw").
        Assert.False(nextCalled);
        await authenticationService.Received(1).ForbidAsync(
            context, Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task HandleAsync_WhenSucceeded_LogsNothingAndCallsNext()
    {
        var logger = Substitute.For<ILogger<LoggingAuthorizationMiddlewareResultHandler>>();
        var handler = new LoggingAuthorizationMiddlewareResultHandler(logger);
        var (context, authenticationService) = BuildHttpContext();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var nextCalled = false;
        Task Next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        await handler.HandleAsync(Next, context, policy, PolicyAuthorizationResult.Success());

        Assert.Empty(logger.ReceivedCalls());
        Assert.True(nextCalled);
        await authenticationService.DidNotReceive().ForbidAsync(
            Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
    }
}
