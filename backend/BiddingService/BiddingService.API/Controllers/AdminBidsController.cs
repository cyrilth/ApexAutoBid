using BiddingService.Application.DTOs;
using BiddingService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingService.API.Controllers;

// ── Phase 11 Task 5.1/5.4 — admin bid moderation (Requirements §10.2/§10.4) ──────────────────
//
// [Authorize(Roles = "admin")] at the controller level covers every action here: an
// unauthenticated caller gets 401 (the framework's Challenge, since RolesAuthorizationRequirement
// runs authentication first), and an authenticated caller who lacks the "admin" role gets 403
// (Forbid) — exactly Requirements §10's "every api/admin/* endpoint returns 403 for non-admin
// callers" (and implicitly, 401 for anonymous). No RoleClaimType override is needed for
// User.IsInRole/[Authorize(Roles=...)] to resolve the "admin" role from a real IdentityServer
// token — see Program.cs's JWT bearer configuration remarks (mirrors AuctionsController's
// identical, already-verified User.IsInRole("admin") usage).

[ApiController]
[Route("api/admin/bids")]
[Authorize(Roles = "admin")]
public class AdminBidsController(IAdminBidService service, ILogger<AdminBidsController> logger) : ControllerBase
{
    // ── 5.1  DELETE api/admin/bids/{id} ───────────────────────────────────────
    //
    // Removes the bid, recalculates the auction's current high bid from its remaining accepted
    // bids, publishes BidRemoved so Auction/Search refresh, and writes an append-only AuditEntry
    // (Requirements §13.3). 404 if no bid with the given id exists.

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveBid(Guid id, CancellationToken cancellationToken)
    {
        var actor = User.Identity!.Name!;

        var outcome = await service.RemoveBidAsync(id, actor, cancellationToken);

        if (outcome == RemoveBidOutcome.NotFound)
        {
            logger.LogWarning("RemoveBid: bid {BidId} not found", id);

            return NotFound(new ProblemDetails
            {
                Title = "Bid not found",
                Detail = $"No bid with id '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok();
    }

    // ── 5.4  GET api/admin/bids/stats ─────────────────────────────────────────
    //
    // Total bid count across every auction (Requirements §10.4 — the admin dashboard's landing
    // page aggregates this alongside the Identity/Auction services' own stats endpoints).

    [HttpGet("stats")]
    public async Task<ActionResult<BidStatsDto>> GetStats(CancellationToken cancellationToken)
    {
        var stats = await service.GetStatsAsync(cancellationToken);

        return Ok(stats);
    }
}
