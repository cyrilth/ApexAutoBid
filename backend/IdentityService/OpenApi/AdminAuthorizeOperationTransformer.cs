using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace IdentityService.OpenApi;

/// <summary>
/// Adds security requirements to operations whose endpoint requires authorization — in practice,
/// every action on <see cref="Controllers.AdminUsersController"/> (the only controller this
/// service has; Razor Pages are not part of AddOpenApi()'s generated document at all). Mirrors
/// AuctionService.API's <c>AuthorizeOperationTransformer</c> of the same shape: two ALTERNATIVE
/// requirements (OAuth2 and Bearer) are attached so either credential type satisfies the
/// requirement in the Scalar UI.
/// </summary>
internal sealed class AdminAuthorizeOperationTransformer(IConfiguration configuration) : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuth = metadata.OfType<IAuthorizeData>().Any()
            && !metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuth)
            return Task.CompletedTask;

        operation.Security ??= new List<OpenApiSecurityRequirement>();

        // Gated on the same condition OAuth2SecuritySchemeTransformer uses to declare the
        // "OAuth2" scheme — see AuctionService.API's identical transformer for why this ordering
        // (operation transformers run before document transformers; the scheme reference is
        // resolved lazily by key against the finished document) matters.
        if (!string.IsNullOrWhiteSpace(configuration["IdentityServiceUrl"]))
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("OAuth2", context.Document)] = new List<string> { "apexautobid" },
            });
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
        });

        return Task.CompletedTask;
    }
}
