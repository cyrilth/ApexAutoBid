using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace AuctionService.API.OpenApi;

/// <summary>
/// Registers an OAuth2 (authorization code + PKCE) security scheme on the generated OpenAPI
/// document, driven by Duende IdentityServer's `scalar` client (Phase 3 Task 13 —
/// IdentityService/Config.cs). Declared alongside — not instead of — the "Bearer" HTTP scheme
/// from <see cref="BearerSecuritySchemeTransformer"/>: paste-a-token remains useful for
/// quick/manual/scripted testing without a browser, while this scheme lets the Scalar UI drive
/// the real interactive login (Requirements.md §3.4/§9, Architecture.md §5.5/§10 — Phase 3's
/// acceptance criterion: "Scalar docs login flow obtains a JWT via IdentityServer (authorization
/// code + PKCE) and authenticated 'try it' requests succeed").
/// <see cref="AuthorizeOperationTransformer"/> attaches BOTH schemes as alternative ("OR")
/// requirements to protected operations.
/// </summary>
internal sealed class OAuth2SecuritySchemeTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Config-driven, not hardcoded (matches AddJwtBearer's Authority below in Program.cs) —
        // if IdentityServiceUrl isn't configured, skip declaring the scheme rather than emit a
        // document with an invalid/empty authorization or token URL.
        var identityServiceUrl = configuration["IdentityServiceUrl"];
        if (string.IsNullOrWhiteSpace(identityServiceUrl))
        {
            return Task.CompletedTask;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["OAuth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Authorization code + PKCE via Duende IdentityServer (the `scalar` client).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{identityServiceUrl}/connect/authorize"),
                    TokenUrl = new Uri($"{identityServiceUrl}/connect/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        ["openid"] = "Sign in",
                        ["profile"] = "Basic profile",
                        ["apexautobid"] = "Access ApexAutoBid backend services",
                    },
                },
            },
        };

        return Task.CompletedTask;
    }
}
