using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BiddingService.API.OpenApi;

/// <summary>
/// Registers a Bearer (JWT) HTTP security scheme on the generated OpenAPI document so the Scalar
/// UI can attach a bearer token to "try it" requests. The built-in generator does not infer
/// security schemes from [Authorize], so the scheme is declared here; the per-endpoint requirement
/// is applied by <see cref="AuthorizeOperationTransformer"/>. Also stamps the document title and
/// version (from the assembly informational version — see Docs/Versioning.md). Copied verbatim
/// from <c>AuctionService.API.OpenApi.BearerSecuritySchemeTransformer</c> (Phase 5 Task 18 —
/// "same pattern as Auction Service"), with only the document title changed.
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
                Description = "Paste a JWT access token (without the \"Bearer\" prefix).",
            };
        }

        document.Info.Title = "Bidding Service API";
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
