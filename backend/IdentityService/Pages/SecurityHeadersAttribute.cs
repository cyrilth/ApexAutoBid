using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityService.Pages;

public sealed class SecurityHeadersAttribute : ActionFilterAttribute
{
    // Phase 3 Task 16.1 — the Turnstile widget (Register page only) needs its script AND its
    // (invisible) challenge iframe allowed through CSP, or it silently fails to render/verify.
    // Deliberately settable per-attribute-usage (e.g. [SecurityHeaders(ExtraScriptSrc = "...",
    // ExtraFrameSrc = "...")] on just Register/Index.cshtml.cs) rather than widened globally —
    // every OTHER page keeps the exact original CSP, unchanged.
    public string? ExtraScriptSrc { get; init; }

    public string? ExtraFrameSrc { get; init; }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));

        var result = context.Result;
        if (result is PageResult)
        {
            // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Content-Type-Options
            if (!context.HttpContext.Response.Headers.ContainsKey("X-Content-Type-Options"))
            {
                context.HttpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            }

            // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Frame-Options
            if (!context.HttpContext.Response.Headers.ContainsKey("X-Frame-Options"))
            {
                context.HttpContext.Response.Headers.Append("X-Frame-Options", "DENY");
            }

            // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy
            var csp = "default-src 'self'; object-src 'none'; frame-ancestors 'none'; sandbox allow-forms allow-same-origin allow-scripts; base-uri 'self';";
            // also consider adding upgrade-insecure-requests once you have HTTPS in place for production
            //csp += "upgrade-insecure-requests;";
            // also an example if you need client images to be displayed from twitter
            // csp += "img-src 'self' https://pbs.twimg.com;";

            // No script-src/frame-src directive existed before Task 16 — both fell back to
            // default-src 'self' (CSP's documented fallback behavior), which silently blocked
            // Turnstile's script AND its challenge iframe. Only added when the attribute usage
            // opts in; every other page's CSP string is byte-for-byte unchanged.
            if (!string.IsNullOrEmpty(ExtraScriptSrc))
            {
                csp += $" script-src 'self' {ExtraScriptSrc};";
            }

            if (!string.IsNullOrEmpty(ExtraFrameSrc))
            {
                csp += $" frame-src {ExtraFrameSrc};";
            }

            // once for standards compliant browsers
            if (!context.HttpContext.Response.Headers.ContainsKey("Content-Security-Policy"))
            {
                context.HttpContext.Response.Headers.Append("Content-Security-Policy", csp);
            }
            // and once again for IE
            if (!context.HttpContext.Response.Headers.ContainsKey("X-Content-Security-Policy"))
            {
                context.HttpContext.Response.Headers.Append("X-Content-Security-Policy", csp);
            }

            // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Referrer-Policy
            var referrer_policy = "no-referrer";
            if (!context.HttpContext.Response.Headers.ContainsKey("Referrer-Policy"))
            {
                context.HttpContext.Response.Headers.Append("Referrer-Policy", referrer_policy);
            }
        }
    }
}
