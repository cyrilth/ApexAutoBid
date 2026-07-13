using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

/// <summary>
/// Public read endpoint for currently-active banner messages (Requirements §10.3 — Phase 11
/// Task 3.5). Anonymous overall — no [Authorize] here, mirroring AuctionsController's GET
/// endpoints.
/// </summary>
[ApiController]
[Route("api/banners")]
public class BannersController(IBannerService service) : ControllerBase
{
    // ── 3.5  GET api/banners?scope=&auctionId= ───────────────────────────────
    //
    // Returns currently-active banners (ActiveFrom <= now <= ActiveUntil), optionally
    // filtered by scope ("Global" | "HomePage" | "Auction") and/or auctionId. An
    // unrecognized scope value yields an empty list rather than a 400 — see
    // BannerAppService.GetActiveAsync's own remarks.

    [HttpGet]
    public async Task<ActionResult<List<BannerDto>>> GetActiveBanners(
        [FromQuery] string? scope, [FromQuery] Guid? auctionId)
    {
        var banners = await service.GetActiveAsync(scope, auctionId);
        return Ok(banners);
    }
}
