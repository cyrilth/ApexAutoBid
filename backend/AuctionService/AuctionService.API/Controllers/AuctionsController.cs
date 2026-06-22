using System.Security.Claims;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

[ApiController]
[Route("api/auctions")]
public class AuctionsController(IAuctionService service, ILogger<AuctionsController> logger)
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

        var resultDto = await service.CreateAuctionAsync(dto, seller, sellerEmail);

        if (resultDto is null)
        {
            logger.LogError("CreateAuction: save failed for seller {Seller}", seller);

            return BadRequest(new ProblemDetails
            {
                Title = "Could not save changes",
                Detail = "The auction could not be saved. Please try again.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        logger.LogInformation("Auction {AuctionId} created by seller {Seller}",
            resultDto.Id, seller);

        return CreatedAtAction(nameof(GetAuctionById), new { id = resultDto.Id }, resultDto);
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
        var result = await service.UpdateAuctionAsync(id, dto, User.Identity!.Name!);

        return result switch
        {
            AuctionWriteResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            }),
            AuctionWriteResult.Forbidden => Forbid(),
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
        var result = await service.DeleteAuctionAsync(id, User.Identity!.Name!);

        return result switch
        {
            AuctionWriteResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            }),
            AuctionWriteResult.Forbidden => Forbid(),
            AuctionWriteResult.SaveFailed => BadRequest(new ProblemDetails
            {
                Title = "Could not save changes",
                Detail = "The auction could not be deleted. Please try again.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok()
        };
    }
}
