using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace BiddingService.API.OpenApi;

/// <summary>
/// Registers an OAuth2 (authorization code + PKCE) security scheme on the generated OpenAPI
/// document, driven by Duende IdentityServer's `scalar` client (IdentityService/Config.cs).
/// Declared alongside — not instead of — the "Bearer" HTTP scheme from
/// <see cref="BearerSecuritySchemeTransformer"/>: paste-a-token remains useful for
/// quick/manual/scripted testing without a browser, while this scheme lets the Scalar UI drive
/// the real interactive login. <see cref="AuthorizeOperationTransformer"/> attaches BOTH schemes
/// as alternative ("OR") requirements to protected operations. Copied verbatim from
/// <c>AuctionService.API.OpenApi.OAuth2SecuritySchemeTransformer</c> (Phase 5 Task 18 — "same
/// pattern as Auction Service").
/// </summary>
internal sealed class OAuth2SecuritySchemeTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Config-driven, not hardcoded (matches AddJwtBearer's Authority in Program.cs) — if
        // IdentityServiceUrl isn't configured, skip declaring the scheme rather than emit a
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
