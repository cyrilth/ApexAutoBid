using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

/// <summary>
/// Admin-only auction moderation endpoints (Requirements §3.1/§10.2 — Phase 11 Task
/// 3.2/3.3/3.7). Every action requires the "admin" role via the "AdminOnly" policy
/// (Program.cs): an anonymous caller gets 401, an authenticated non-admin gets 403.
/// </summary>
[ApiController]
[Route("api/admin/auctions")]
[Authorize(Policy = "AdminOnly")]
public class AdminAuctionsController(
    IAdminAuctionService service,
    ILogger<AdminAuctionsController> logger) : ControllerBase
{
    // ── 3.2  POST api/admin/auctions/{id}/end ────────────────────────────────
    //
    // Sets AuctionEnd = UtcNow immediately and emits AuctionUpdated (with the new
    // AuctionEnd, and every other item field populated straight from the entity — see
    // AdminAuctionAppService.EndAuctionAsync). The Bidding Service's background
    // finalization job then finalizes the now-expired auction through its normal polling
    // flow (winner, AuctionFinished, notifications) — no direct call is made from here.

    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> EndAuction(Guid id)
    {
        var admin = User.Identity!.Name!;
        var result = await service.EndAuctionAsync(id, admin);

        if (result == AdminAuctionWriteResult.NotFound)
        {
            logger.LogWarning("Admin {Admin} attempted to end unknown auction {AuctionId}", admin, id);
            return NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        logger.LogInformation("Admin {Admin} ended auction {AuctionId} immediately", admin, id);
        return Ok();
    }

    // ── 3.3  POST api/admin/auctions/{id}/cancel ─────────────────────────────
    //
    // Sets Status = Cancelled and emits AuctionCancelled. Bidding refuses further bids and
    // never emits AuctionFinished for a cancelled auction; Search marks it cancelled;
    // Notification broadcasts and informs the seller.

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelAuction(Guid id)
    {
        var admin = User.Identity!.Name!;
        var result = await service.CancelAuctionAsync(id, admin);

        if (result == AdminAuctionWriteResult.NotFound)
        {
            logger.LogWarning("Admin {Admin} attempted to cancel unknown auction {AuctionId}", admin, id);
            return NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        logger.LogInformation("Admin {Admin} cancelled auction {AuctionId}", admin, id);
        return Ok();
    }

    // ── 3.7  GET api/admin/auctions/stats ─────────────────────────────────────

    [HttpGet("stats")]
    public async Task<ActionResult<AuctionStatsDto>> GetStats()
    {
        var stats = await service.GetStatsAsync();
        return Ok(stats);
    }
}
