using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>POST api/admin/banners</c> (Requirements §10.3 — Phase 11 Task 3.5).
/// <see cref="Scope"/> must be exactly "Global", "HomePage", or "Auction" (ordinal, case-
/// sensitive — validated in <c>BannerAppService</c> since <c>[EnumDataType]</c> cannot express
/// the accompanying AuctionId cross-field rule). <see cref="AuctionId"/> is required when
/// <see cref="Scope"/> is "Auction" and must be omitted otherwise.
/// </summary>
public class CreateBannerDto
{
    [Required]
    public required string Message { get; init; }

    [Required]
    public required string Scope { get; init; }

    public Guid? AuctionId { get; init; }

    public DateTime ActiveFrom { get; init; }

    public DateTime ActiveUntil { get; init; }
}
