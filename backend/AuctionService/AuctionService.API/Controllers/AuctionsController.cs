using System.Globalization;
using System.Security.Claims;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

[ApiController]
[Route("api/auctions")]
public class AuctionsController(
    IAuctionService service,
    IAuctionImageService imageService,
    ILogger<AuctionsController> logger)
    : ControllerBase
{
    // ── 8.1 / 8.3  GET api/auctions[?date=] ──────────────────────────────────
    //
    // When `date` is supplied, returns only auctions with UpdatedAt strictly
    // greater than the parsed UTC date. When omitted, returns all auctions.
    // Results are ordered by Make then Model for deterministic listing order.

    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> GetAllAuctions([FromQuery] string? date)
    {
        DateTime? updatedAfter = null;

        if (!string.IsNullOrWhiteSpace(date))
        {
            // Parse with InvariantCulture (not the host's current culture) and UTC semantics:
            // AssumeUniversal treats an offset-less value as UTC; AdjustToUniversal converts an
            // offset-bearing value to UTC rather than to the host's local time. The result already
            // carries Kind=Utc, so no SpecifyKind relabel — SpecifyKind only changes the Kind flag
            // without converting, which would mislabel a local-converted instant as UTC on a non-UTC host.
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid date parameter",
                    Detail = $"'{date}' could not be parsed as a valid date/time.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            updatedAfter = parsedDate; // already normalized to UTC (Kind=Utc)
        }

        var auctions = await service.GetAuctionsAsync(updatedAfter);

        logger.LogDebug("Returning {Count} auctions", auctions.Count);

        return Ok(auctions);
    }

    // ── 8.2 / 19.1  GET api/auctions/{id} ────────────────────────────────────
    //
    // Returns the full auction including the complete image gallery. 404 if the auction does
    // not exist. Remains anonymous overall (no [Authorize] here) — but Phase 5 Task 19's
    // post-sale contact exchange (Requirements §3.1) needs the caller's identity WHEN present,
    // so this endpoint reads User.Identity?.Name directly rather than relying on User.Identity!.Name
    // the way the write endpoints do. This works because JWT bearer authentication runs
    // (UseAuthentication) for every request regardless of [Authorize] — an anonymous caller (no
    // token, or TestAuthHandler's NoResult() in tests) ends up with an unauthenticated principal
    // whose Identity.Name is null, while a caller presenting a valid token gets their username
    // claim populated here even though this action never demands authorization. All of the
    // actual redaction logic lives in AuctionAppService.GetAuctionByIdAsync — this controller
    // only forwards the (possibly null) username.

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuctionDetailDto>> GetAuctionById(Guid id)
    {
        var requestingUser = User.Identity?.Name;

        var dto = await service.GetAuctionByIdAsync(id, requestingUser);

        if (dto is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(dto);
    }

    // ── 8.4  POST api/auctions ────────────────────────────────────────────────
    //
    // Creates a new auction. Requires authentication.
    //
    // Claim-stamping: Seller comes from User.Identity!.Name which maps to the
    // "username" claim (NameClaimType is set to "username" in Program.cs).
    // SellerEmail comes from the standard ClaimTypes.Email ("email") claim.
    //
    // email_verified enforcement (§3.1/§3.4): only verified accounts may create auctions —
    // enforced by the "EmailVerified" authorization policy (Program.cs, Phase 3 Task 19),
    // which replaces the ad-hoc in-body check this endpoint used to do itself. A caller failing
    // the policy never reaches this method body at all — ASP.NET Core's authorization
    // middleware short-circuits to a framework Forbid() (403, no response body) before model
    // binding/the action runs, unlike the old ad-hoc check's handcrafted ProblemDetails 403.

    [Authorize(Policy = "EmailVerified")]
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> CreateAuction([FromBody] CreateAuctionDto dto)
    {
        var seller = User.Identity!.Name!;
        var sellerEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        var result = await service.CreateAuctionAsync(dto, seller, sellerEmail, User.IsInRole("admin"));

        if (result.Status == AuctionWriteResult.Success)
        {
            logger.LogInformation("Auction {AuctionId} created by seller {Seller}",
                result.Auction!.Id, seller);

            return CreatedAtAction(nameof(GetAuctionById), new { id = result.Auction.Id }, result.Auction);
        }

        if (result.Status == AuctionWriteResult.InvalidImages)
        {
            logger.LogWarning("CreateAuction: invalid image gallery for seller {Seller}", seller);

            return BadRequest(new ProblemDetails
            {
                Title = "Invalid image gallery",
                Detail = "One or more images are invalid — check the gallery size (1–10 images) " +
                         "and that no platform-hosted image exceeds the configured size limit.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        logger.LogError("CreateAuction: save failed for seller {Seller}", seller);

        return BadRequest(new ProblemDetails
        {
            Title = "Could not save changes",
            Detail = "The auction could not be saved. Please try again.",
            Status = StatusCodes.Status400BadRequest
        });
    }

    // ── 8.5  PUT api/auctions/{id} ────────────────────────────────────────────
    //
    // Partially updates an existing auction. Only non-null DTO fields are applied.
    // Gallery swap: when dto.Images is non-null, replaces the existing image rows.
    // Ownership: only the seller may update. Returns 403 if caller is not the seller.
    //
    // email_verified enforcement (Phase 3 Task 19): the "EmailVerified" policy (Program.cs) is
    // IN ADDITION TO the ownership check below, not instead of it — an authenticated, verified
    // caller who isn't the auction's seller (or an admin) still gets AuctionWriteResult.Forbidden
    // -> Forbid() from the ownership logic in AuctionAppService, unchanged by this task.

    [Authorize(Policy = "EmailVerified")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAuction(Guid id, [FromBody] UpdateAuctionDto dto)
    {
        var result = await service.UpdateAuctionAsync(id, dto, User.Identity!.Name!, User.IsInRole("admin"));

        return result switch
        {
            AuctionWriteResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            }),
            AuctionWriteResult.Forbidden => Forbid(),
            AuctionWriteResult.InvalidImages => BadRequest(new ProblemDetails
            {
                Title = "Invalid image gallery",
                Detail = "One or more images are invalid — check the gallery size (1–10 images) " +
                         "and that no platform-hosted image exceeds the configured size limit.",
                Status = StatusCodes.Status400BadRequest
            }),
            AuctionWriteResult.SaveFailed => BadRequest(new ProblemDetails
            {
                Title = "Could not save changes",
                Detail = "The auction could not be updated. Please try again.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok()
        };
    }

    // ── 8.6  DELETE api/auctions/{id} ─────────────────────────────────────────
    //
    // Deletes an existing auction. Requires authentication.
    // 404 if not found, 403 if caller is not the seller.
    //
    // email_verified enforcement (Phase 3 Task 19): same as UpdateAuction above — the
    // "EmailVerified" policy is in addition to, not instead of, the ownership check.

    [Authorize(Policy = "EmailVerified")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAuction(Guid id)
    {
        var result = await service.DeleteAuctionAsync(id, User.Identity!.Name!, User.IsInRole("admin"));

        return result switch
        {
            AuctionWriteResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            }),
            AuctionWriteResult.Forbidden => Forbid(),
            // InvalidImages is unreachable for delete (no gallery is submitted) but is
            // handled here for exhaustiveness with the shared AuctionWriteResult enum.
            AuctionWriteResult.InvalidImages => BadRequest(new ProblemDetails
            {
                Title = "Invalid image gallery",
                Detail = "One or more images are invalid — check the gallery size (1–10 images) " +
                         "and that no platform-hosted image exceeds the configured size limit.",
                Status = StatusCodes.Status400BadRequest
            }),
            AuctionWriteResult.SaveFailed => BadRequest(new ProblemDetails
            {
                Title = "Could not save changes",
                Detail = "The auction could not be deleted. Please try again.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok()
        };
    }

    // ── 18.1  POST api/auctions/upload-url ────────────────────────────────────
    //
    // Issues a 5-minute presigned PUT URL for a client-declared content type and size.
    // Requires authentication and a verified email. Image bytes never flow through this
    // service — the client PUTs directly to the presigned URL, then submits the returned
    // ObjectUrl in a create/update auction request.
    //
    // email_verified enforcement (Phase 3 Task 19 follow-up): this endpoint's ad-hoc in-body
    // check has been converted to the "EmailVerified" policy (Program.cs), same as
    // CreateAuction/UpdateAuction/DeleteAuction — it is now THE single mechanism gating
    // email-verified across all five mutating endpoints, not one of several independent copies.
    // A caller failing the policy never reaches this method body — see CreateAuction's remarks
    // for the exact framework Forbid()-vs-old-handcrafted-ProblemDetails behavior change.

    [Authorize(Policy = "EmailVerified")]
    [HttpPost("upload-url")]
    public async Task<ActionResult<UploadUrlResponse>> CreateUploadUrl([FromBody] UploadUrlRequest request)
    {
        var (outcome, response) = await imageService.CreateUploadUrlAsync(request);

        return outcome switch
        {
            UploadUrlOutcome.InvalidContentType => BadRequest(new ProblemDetails
            {
                Title = "Invalid content type",
                Detail = $"Content type '{request.ContentType}' is not allowed. " +
                         "Allowed types: image/jpeg, image/png, image/webp.",
                Status = StatusCodes.Status400BadRequest
            }),
            UploadUrlOutcome.InvalidSize => BadRequest(new ProblemDetails
            {
                Title = "Invalid file size",
                Detail = "The declared file size must be greater than zero and within the " +
                         "configured per-image limit.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok(response)
        };
    }

    // ── 18.4  POST api/auctions/thumbnail ─────────────────────────────────────
    //
    // Generates a max-400px-wide WebP thumbnail for a previously uploaded object key.
    // Requires authentication and a verified email. SSRF guard: only a bare GUID key
    // (the format upload-url issues) is accepted — see AuctionImageAppService.
    //
    // email_verified enforcement (Phase 3 Task 19 follow-up): converted to the "EmailVerified"
    // policy — see CreateUploadUrl's remarks above.

    [Authorize(Policy = "EmailVerified")]
    [HttpPost("thumbnail")]
    public async Task<ActionResult<ThumbnailResponse>> CreateThumbnail([FromBody] ThumbnailRequest request)
    {
        var (outcome, response) = await imageService.CreateThumbnailAsync(request.Key);

        return outcome switch
        {
            ThumbnailOutcome.InvalidKey => BadRequest(new ProblemDetails
            {
                Title = "Invalid image key",
                Detail = "The image key must be a GUID returned by a prior upload-url call.",
                Status = StatusCodes.Status400BadRequest
            }),
            ThumbnailOutcome.SourceNotFound => NotFound(new ProblemDetails
            {
                Title = "Source image not found",
                Detail = $"No uploaded object was found for key '{request.Key}'.",
                Status = StatusCodes.Status404NotFound
            }),
            _ => Ok(response)
        };
    }
}
