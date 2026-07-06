using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace AuctionService.API.Handlers;

/// <summary>
/// Wraps the framework's default <see cref="AuthorizationMiddlewareResultHandler"/> to restore
/// Warning-level rejection logging for authorization POLICY failures (Phase 3 Task 19 follow-up).
/// <para>
/// Before Task 19, all five email-verified gates (CreateAuction/UpdateAuction/DeleteAuction/
/// CreateUploadUrl/CreateThumbnail) were ad-hoc in-body checks that each logged a Warning
/// (username only, never the email claim value — Requirements §13.5) on rejection. Moving that
/// enforcement into the "EmailVerified" authorization POLICY (Program.cs) made those rejections
/// completely silent: decompiled end-to-end (AuthorizationMiddleware -&gt; PolicyEvaluator -&gt;
/// AuthorizationMiddlewareResultHandler), a RequireClaim requirement failure logs NOTHING at any
/// level — the default handler's only job is to call ChallengeAsync/ForbidAsync, never
/// <see cref="ILogger"/>. This class restores that parity without reimplementing the default
/// handler's own Challenge-vs-Forbid logic at all: it delegates to a real
/// <see cref="AuthorizationMiddlewareResultHandler"/> instance for the actual response, only
/// adding a log line first. That default handler is decompile-confirmed stateless (no fields, no
/// explicit constructor), so constructing and reusing one instance here is safe.
/// </para>
/// <para>
/// Deliberately does NOT log on <see cref="PolicyAuthorizationResult.Challenged"/> (401,
/// anonymous/unauthenticated caller) — the old ad-hoc checks never logged the "no token at all"
/// case either (that was always JwtBearer's own 401 short-circuit, not an app-level
/// warning-worthy event). Only <see cref="PolicyAuthorizationResult.Forbidden"/> (an
/// AUTHENTICATED caller who failed a policy requirement — e.g. email_verified=false) is logged,
/// matching the old checks' exact trigger condition and log level.
/// </para>
/// <para>
/// Controller-level <c>Forbid()</c> calls — the ownership-failure paths in
/// UpdateAuction/DeleteAuction, via AuctionAppService's <c>AuctionWriteResult.Forbidden</c> —
/// do NOT flow through this handler at all: they call <c>HttpContext</c>'s <c>ForbidAsync</c>
/// directly from inside the action method, after the authorization middleware already ran and
/// succeeded (the caller passed the "EmailVerified" policy; they're just not the auction's
/// seller). This handler therefore only ever logs POLICY rejections — never ownership
/// rejections — which is exactly the parity being restored, not a new, broader logging surface.
/// </para>
/// </summary>
public class LoggingAuthorizationMiddlewareResultHandler(ILogger<LoggingAuthorizationMiddlewareResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    // Shared/static because it's provably stateless (see this class's remarks) — no need for a
    // fresh instance per HandleAsync call, regardless of this class's own DI lifetime.
    private static readonly AuthorizationMiddlewareResultHandler DefaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            // Username only (User.Identity?.Name -> the "username" claim, via NameClaimType in
            // this same Program.cs) — never the email claim value (Requirements §13.5), matching
            // exactly what the old ad-hoc checks logged.
            logger.LogWarning(
                "Authorization policy rejected {Method} {Path} for user {User}",
                context.Request.Method, context.Request.Path, context.User.Identity?.Name);
        }

        return DefaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
