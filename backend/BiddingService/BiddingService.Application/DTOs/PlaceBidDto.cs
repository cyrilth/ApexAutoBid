using System.ComponentModel.DataAnnotations;

namespace BiddingService.Application.DTOs;

/// <summary>Input DTO for <c>POST api/bids</c> (Requirements §3.3).</summary>
public class PlaceBidDto
{
    // No [Required] on a value type — a non-nullable Guid can never be null (the attribute
    // would be a no-op and misleadingly imply Guid.Empty is rejected). An empty/garbage
    // AuctionId simply fails to resolve via IAuctionProvider and surfaces as the ordinary
    // AuctionNotFound (404) outcome — no separate validation is needed here.
    public Guid AuctionId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public int Amount { get; init; }
}
