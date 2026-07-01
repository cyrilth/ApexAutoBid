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
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid date parameter",
                    Detail = $"'{date}' could not be parsed as a valid date/time.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Treat the supplied date as UTC and filter strictly greater-than.
            updatedAfter = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var auctions = await service.GetAuctionsAsync(updatedAfter);

        logger.LogDebug("Returning {Count} auctions", auctions.Count);

        return Ok(auctions);
    }

    // ── 8.2  GET api/auctions/{id} ───────────────────────────────────────────
    //
    // Returns the full auction including the complete image gallery.
    // 404 if the auction does not exist.

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuctionDto>> GetAuctionById(Guid id)
    {
        var dto = await service.GetAuctionByIdAsync(id);

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
    // email_verified enforcement (§3.1/§3.4): only verified accounts may create
    // auctions. The "email_verified" claim must be present and equal "true".

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> CreateAuction([FromBody] CreateAuctionDto dto)
    {
        // Enforce email_verified before any DB work.
        var emailVerified = User.FindFirstValue("email_verified");
        if (emailVerified is not "true")
        {
            logger.LogWarning("Auction creation blocked — email not verified for user {User}",
                User.Identity!.Name);

            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Email not verified",
                Detail = "You must verify your email address before creating an auction.",
                Status = StatusCodes.Status403Forbidden
            });
        }

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

    [Authorize]
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

    [Authorize]
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
    // Requires authentication and a verified email (same enforcement as CreateAuction).
    // Image bytes never flow through this service — the client PUTs directly to the
    // presigned URL, then submits the returned ObjectUrl in a create/update auction request.

    [Authorize]
    [HttpPost("upload-url")]
    public async Task<ActionResult<UploadUrlResponse>> CreateUploadUrl([FromBody] UploadUrlRequest request)
    {
        var emailVerified = User.FindFirstValue("email_verified");
        if (emailVerified is not "true")
        {
            logger.LogWarning("upload-url blocked — email not verified for user {User}",
                User.Identity!.Name);

            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Email not verified",
                Detail = "You must verify your email address before uploading images.",
                Status = StatusCodes.Status403Forbidden
            });
        }

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
            UploadUrlOutcome.TooLarge => BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = "The declared file size exceeds the configured per-image limit.",
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

    [Authorize]
    [HttpPost("thumbnail")]
    public async Task<ActionResult<ThumbnailResponse>> CreateThumbnail([FromBody] ThumbnailRequest request)
    {
        var emailVerified = User.FindFirstValue("email_verified");
        if (emailVerified is not "true")
        {
            logger.LogWarning("thumbnail blocked — email not verified for user {User}",
                User.Identity!.Name);

            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Email not verified",
                Detail = "You must verify your email address before generating thumbnails.",
                Status = StatusCodes.Status403Forbidden
            });
        }

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
