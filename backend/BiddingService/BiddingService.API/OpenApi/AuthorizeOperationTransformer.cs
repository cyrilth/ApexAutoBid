using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace BiddingService.API.OpenApi;

/// <summary>
/// Adds security requirements to operations whose endpoint requires authorization (has
/// [Authorize] metadata and is not [AllowAnonymous]) — today, only <c>POST api/bids</c>
/// (<c>[Authorize(Policy = "EmailVerified")]</c>). <c>GET api/bids/{auctionId}</c> is left
/// unmarked so the Scalar UI shows only the write endpoint as protected. Copied verbatim from
/// <c>AuctionService.API.OpenApi.AuthorizeOperationTransformer</c> (Phase 5 Task 18 — "same
/// pattern as Auction Service") — see that class's remarks for the full rationale, unchanged
/// here.
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
