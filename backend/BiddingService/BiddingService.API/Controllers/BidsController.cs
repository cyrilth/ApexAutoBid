using System.Security.Claims;
using BiddingService.Application.DTOs;
using BiddingService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingService.API.Controllers;

[ApiController]
[Route("api/bids")]
public class BidsController(IBidService service, ILogger<BidsController> logger) : ControllerBase
{
    // ── 9.1  POST api/bids ────────────────────────────────────────────────────
    //
    // Places a bid. Requires authentication AND the "EmailVerified" policy (Program.cs) —
    // 403 when the caller's email_verified claim isn't "true" (Requirements §3.3/§3.4),
    // mirroring AuctionsController's identical policy usage on its mutating endpoints. A
    // caller failing the policy never reaches this method body — ASP.NET Core's authorization
    // middleware short-circuits to a framework Forbid() (403, no response body) before model
    // binding/the action runs.
    //
    // Claim-stamping: Bidder comes from User.Identity!.Name, which maps to the "username"
    // claim (NameClaimType is set to "username" in Program.cs, matching AuctionsController's
    // identical Seller-stamping convention). BidderEmail comes from the standard
    // ClaimTypes.Email ("email") claim — captured only for the later post-sale
    // AuctionFinished.WinnerEmail flow; never returned in this endpoint's response.
    //
    // All four possible outcomes (Accepted/AcceptedBelowReserve/TooLow/Finished) return 200
    // OK with the outcome carried in BidDto.BidStatus — Requirements §3.3 / Task 21: none of
    // these are HTTP errors. Only a not-found auction (404) or a seller bidding on their own
    // auction (400) short-circuit before a bid is even recorded.

    [Authorize(Policy = "EmailVerified")]
    [HttpPost]
    public async Task<ActionResult<BidDto>> PlaceBid(
        [FromBody] PlaceBidDto dto, CancellationToken cancellationToken)
    {
        var bidder = User.Identity!.Name!;
        var bidderEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        var (outcome, bid) = await service.PlaceBidAsync(dto, bidder, bidderEmail, cancellationToken);

        return outcome switch
        {
            BidOutcome.AuctionNotFound => NotFound(new ProblemDetails
            {
                Title = "Auction not found",
                Detail = $"No auction with id '{dto.AuctionId}' was found.",
                Status = StatusCodes.Status404NotFound
            }),
            BidOutcome.SellerCannotBid => BadRequest(new ProblemDetails
            {
                Title = "Sellers cannot bid on their own auction",
                Detail = "The seller of an auction is not permitted to place a bid on it.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok(bid)
        };
    }

    // ── 9.2  GET api/bids/{auctionId} ─────────────────────────────────────────
    //
    // Anonymous (Requirements §3.3) — no [Authorize] attribute at all, matching
    // AuctionsController's identical convention for its own anonymous GET endpoints. Returns
    // every bid for the auction, newest first; BidDto never carries BidderEmail.

    [HttpGet("{auctionId:guid}")]
    public async Task<ActionResult<List<BidDto>>> GetBidsForAuction(
        Guid auctionId, CancellationToken cancellationToken)
    {
        var bids = await service.GetBidsForAuctionAsync(auctionId, cancellationToken);

        logger.LogDebug("Returning {Count} bids for auction {AuctionId}", bids.Count, auctionId);

        return Ok(bids);
    }
}
