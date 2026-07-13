using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace IdentityService.OpenApi;

/// <summary>
/// Registers an OAuth2 (authorization code + PKCE) security scheme on this service's own admin
/// API OpenAPI document, driven by the same `scalar` client (Config.cs) every other service's
/// docs page already authenticates against. Declared alongside — not instead of — the "Bearer"
/// HTTP scheme (<see cref="BearerSecuritySchemeTransformer"/>), mirroring
/// AuctionService.API/BiddingService.API's identical pattern exactly.
/// <see cref="AdminAuthorizeOperationTransformer"/> attaches BOTH schemes as alternative ("OR")
/// requirements to protected operations.
/// </summary>
internal sealed class OAuth2SecuritySchemeTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Config-driven, matching AddJwtBearer's Authority in HostingExtensions.cs — this service
        // IS the IdentityServer, so IdentityServiceUrl here is its own base URL
        // (https://localhost:5001 in dev, appsettings.Development.json).
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
            Description = "Authorization code + PKCE via Duende IdentityServer (the `scalar` client). " +
                          "The signed-in account must hold the \"admin\" role for these operations to succeed.",
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
