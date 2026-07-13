using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace IdentityService.OpenApi;

/// <summary>
/// Registers a Bearer (JWT) HTTP security scheme on this service's OWN OpenAPI document (Phase
/// 11 Task 2.8 — the admin API's <c>/openapi/v1.json</c>, distinct from the OIDC/OAuth protocol
/// surface Duende itself serves). Mirrors AuctionService.API's identical transformer of the same
/// name byte-for-byte in intent: the built-in generator does not infer a security scheme from
/// [Authorize], so it is declared here; the per-endpoint requirement is applied by
/// <see cref="AdminAuthorizeOperationTransformer"/>. Also stamps the document title/version.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemes = await schemeProvider.GetAllSchemesAsync();
        if (schemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "Paste a JWT access token carrying the \"admin\" role (without the \"Bearer\" prefix).",
            };
        }

        document.Info.Title = "Identity Service Admin API";
        document.Info.Version = ResolveVersion();
    }

    private static string ResolveVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "0.1.0" : version;
    }
}
