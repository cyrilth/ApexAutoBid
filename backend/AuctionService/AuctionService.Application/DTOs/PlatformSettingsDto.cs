namespace AuctionService.Application.DTOs;

/// <summary>
/// Read DTO for <c>GET api/admin/settings/duration</c> (Phase 11 Task 3.8). When no admin
/// override has ever been saved, <see cref="MinDuration"/>/<see cref="MaxDuration"/> reflect
/// the configuration/environment-variable defaults and <see cref="UpdatedBy"/>/
/// <see cref="UpdatedAt"/> are <see langword="null"/>.
/// </summary>
public class PlatformSettingsDto
{
    public required TimeSpan MinDuration { get; init; }
    public required TimeSpan MaxDuration { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
