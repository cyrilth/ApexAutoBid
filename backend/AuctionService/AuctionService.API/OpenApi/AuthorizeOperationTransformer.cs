using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace AuctionService.API.OpenApi;

/// <summary>
/// Adds security requirements to operations whose endpoint requires authorization (has
/// [Authorize] metadata and is not [AllowAnonymous]). Anonymous endpoints (the GETs) are left
/// unmarked so the Scalar UI shows only the write endpoints as protected.
/// <para>
/// Two ALTERNATIVE requirements are added (Phase 3 Task 13) — OAuth2 (the "apexautobid" scope,
/// see <see cref="OAuth2SecuritySchemeTransformer"/>) and Bearer (paste-a-token, see
/// <see cref="BearerSecuritySchemeTransformer"/>). An operation's <c>Security</c> list is a
/// logical OR between its entries (each entry itself is an AND of the schemes it lists), so
/// either credential type satisfies the requirement — the interactive OAuth2 flow is now the
/// primary way to authenticate from the docs UI, with Bearer kept as a secondary, manual option
/// rather than removed.
/// </para>
/// </summary>
internal sealed class AuthorizeOperationTransformer(IConfiguration configuration) : IOpenApiOperationTransformer
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
        // "OAuth2" scheme: operation transformers run BEFORE document transformers, and the
        // scheme reference is resolved lazily by key against the finished document — so if
        // IdentityServiceUrl is unset the scheme is never declared, and adding the requirement
        // here would leave a dangling $ref in the generated document.
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
