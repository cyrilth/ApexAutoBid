namespace AuctionService.Application.DTOs;

/// <summary>
/// Read DTO for banner messages (Requirements §10.3 — Phase 11 Task 3.5). <see cref="Scope"/>
/// is a string ("Global" | "HomePage" | "Auction") rather than the Domain
/// <c>BannerScope</c> enum, mirroring <see cref="AuctionDto.Status"/>'s own decoupling rationale.
/// </summary>
public class BannerDto
{
    public Guid Id { get; init; }
    public required string Message { get; init; }
    public required string Scope { get; init; }
    public Guid? AuctionId { get; init; }
    public DateTime ActiveFrom { get; init; }
    public DateTime ActiveUntil { get; init; }
    public required string CreatedBy { get; init; }
}
