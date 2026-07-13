using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>PUT api/admin/banners/{id}</c> (Requirements §10.3 — Phase 11 Task 3.5).
/// Unlike <see cref="AuctionService.Application.DTOs.UpdateAuctionDto"/>, this is a full
/// replace, not a partial update — a banner has few, always-related fields (Scope/AuctionId
/// are a single cross-field rule), so a partial-update "null means unchanged" DTO would only
/// add ambiguity (e.g. explicitly clearing <see cref="AuctionId"/> back to null when narrowing
/// scope away from "Auction") for no real benefit here.
/// </summary>
public class UpdateBannerDto
{
    [Required]
    public required string Message { get; init; }

    [Required]
    public required string Scope { get; init; }

    public Guid? AuctionId { get; init; }

    public DateTime ActiveFrom { get; init; }

    public DateTime ActiveUntil { get; init; }
}
