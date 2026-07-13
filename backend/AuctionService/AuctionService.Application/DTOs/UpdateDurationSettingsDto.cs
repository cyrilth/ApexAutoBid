namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>PUT api/admin/settings/duration</c> (Phase 11 Task 3.8). Both fields are
/// required (this is a full replace of the single settings row, not a partial update); no
/// <c>[Required]</c> attribute is added to either — both are non-nullable value types, so the
/// attribute would be a no-op (matches the codebase's existing convention — see
/// <c>CreateAuctionDto.Mileage</c>'s own remarks). Range validation (positive, Min &lt; Max)
/// is enforced in <c>PlatformSettingsAppService</c>, not here, because it is a cross-field rule
/// DataAnnotations cannot express on a single property.
/// </summary>
public class UpdateDurationSettingsDto
{
    public TimeSpan MinDuration { get; init; }
    public TimeSpan MaxDuration { get; init; }
}
