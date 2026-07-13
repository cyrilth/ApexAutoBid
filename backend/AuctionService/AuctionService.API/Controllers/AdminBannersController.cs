using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

/// <summary>
/// Admin CRUD for banner messages (Requirements §10.3 — Phase 11 Task 3.5). Every action
/// requires the "admin" role via the "AdminOnly" policy: 401 anonymous, 403 non-admin.
/// </summary>
[ApiController]
[Route("api/admin/banners")]
[Authorize(Policy = "AdminOnly")]
public class AdminBannersController(
    IBannerService service,
    ILogger<AdminBannersController> logger) : ControllerBase
{
    // ── 3.5  GET api/admin/banners ────────────────────────────────────────────
    //
    // Admin listing: every banner regardless of its active window (unlike the public
    // BannersController, which only ever returns currently-active ones).

    [HttpGet]
    public async Task<ActionResult<List<BannerDto>>> GetAllBanners()
    {
        var banners = await service.GetAllAsync();
        return Ok(banners);
    }

    // ── 3.5  POST api/admin/banners ───────────────────────────────────────────
    //
    // Emits BannerPublished via the outbox (BannerAppService.CreateAsync) and writes an
    // append-only AuditEntry ("BannerCreated") in the same SaveChanges (Requirements §13.3).

    [HttpPost]
    public async Task<ActionResult<BannerDto>> CreateBanner([FromBody] CreateBannerDto dto)
    {
        var admin = User.Identity!.Name!;
        var result = await service.CreateAsync(dto, admin);

        var problem = ProblemFor(result.Status);
        if (problem is not null)
            return problem;

        logger.LogInformation("Admin {Admin} created banner {BannerId}", admin, result.Banner!.Id);
        return StatusCode(StatusCodes.Status201Created, result.Banner);
    }

    // ── 3.5  PUT api/admin/banners/{id} ───────────────────────────────────────
    //
    // Full replace (see UpdateBannerDto's own remarks). Emits BannerPublished via the outbox
    // and writes an append-only AuditEntry ("BannerUpdated") in the same SaveChanges.

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBanner(Guid id, [FromBody] UpdateBannerDto dto)
    {
        var admin = User.Identity!.Name!;
        var status = await service.UpdateAsync(id, dto, admin);

        if (status == BannerWriteResult.NotFound)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Banner not found",
                Detail = $"No banner with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var problem = ProblemFor(status);
        if (problem is not null)
            return problem;

        logger.LogInformation("Admin {Admin} updated banner {BannerId}", admin, id);
        return Ok();
    }

    // ── 3.5  DELETE api/admin/banners/{id} ────────────────────────────────────
    //
    // No event is emitted on delete (only create/update publish BannerPublished). Writes an
    // append-only AuditEntry ("BannerDeleted") in the same SaveChanges.

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBanner(Guid id)
    {
        var admin = User.Identity!.Name!;
        var status = await service.DeleteAsync(id, admin);

        if (status == BannerWriteResult.NotFound)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Banner not found",
                Detail = $"No banner with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        logger.LogInformation("Admin {Admin} deleted banner {BannerId}", admin, id);
        return Ok();
    }

    // Maps the Scope/AuctionId/date-range validation failures BannerAppService can return for
    // Create/Update to a 400 ProblemDetails. Returns null for Success (and NotFound, which the
    // callers above already handle themselves) so each caller only substitutes its own result.
    private static ObjectResult? ProblemFor(BannerWriteResult status) => status switch
    {
        BannerWriteResult.InvalidScope => BadRequestProblem(
            "Invalid scope", "Scope must be exactly one of 'Global', 'HomePage', or 'Auction'."),
        BannerWriteResult.MissingAuctionId => BadRequestProblem(
            "Missing auction id", "AuctionId is required when Scope is 'Auction'."),
        BannerWriteResult.UnexpectedAuctionId => BadRequestProblem(
            "Unexpected auction id", "AuctionId must be omitted unless Scope is 'Auction'."),
        BannerWriteResult.InvalidDateRange => BadRequestProblem(
            "Invalid date range", "ActiveFrom must be strictly earlier than ActiveUntil."),
        _ => null
    };

    private static ObjectResult BadRequestProblem(string title, string detail) => new(new ProblemDetails
    {
        Title = title,
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    })
    { StatusCode = StatusCodes.Status400BadRequest };
}
