using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.API.Controllers;

/// <summary>
/// Admin-only platform duration settings (Requirements §3.1/§10.2 — Phase 11 Task 3.8).
/// GET/PUT require the "admin" role via the "AdminOnly" policy: 401 anonymous, 403 non-admin.
/// PUT takes effect immediately — <see cref="PlatformSettingsAppService"/> reads the DB row
/// fresh on every call (no caching), so no restart is required.
/// </summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = "AdminOnly")]
public class AdminSettingsController(
    IPlatformSettingsService service,
    ILogger<AdminSettingsController> logger) : ControllerBase
{
    [HttpGet("duration")]
    public async Task<ActionResult<PlatformSettingsDto>> GetDurationSettings()
    {
        var settings = await service.GetDurationSettingsAsync();
        return Ok(settings);
    }

    [HttpPut("duration")]
    public async Task<IActionResult> UpdateDurationSettings([FromBody] UpdateDurationSettingsDto dto)
    {
        var admin = User.Identity!.Name!;
        var result = await service.UpdateDurationSettingsAsync(dto, admin);

        if (result.Status == PlatformSettingsWriteResult.InvalidRange)
        {
            logger.LogWarning("Admin {Admin} submitted an invalid duration range", admin);
            ModelState.AddModelError(nameof(dto.MinDuration),
                "MinDuration must be positive and strictly less than MaxDuration.");
            return ValidationProblem(ModelState);
        }

        logger.LogInformation("Admin {Admin} updated the platform auction duration bounds", admin);
        return Ok(result.Settings);
    }
}
