using IdentityService.Dtos.Admin;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers;

/// <summary>
/// Admin user-management API (Phase 11 Task 2 / Requirements.md §10.1). Every action requires
/// the "AdminOnly" policy (HostingExtensions.cs — Bearer scheme + the "admin" role claim):
/// an anonymous caller gets 401, an authenticated non-admin caller gets 403. DTOs only — never
/// exposes <see cref="Models.ApplicationUser"/> (the Identity entity) directly.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "AdminOnly")]
public class AdminUsersController(IAdminUserService adminUserService, ILogger<AdminUsersController> logger)
    : ControllerBase
{
    // ── GET api/admin/users ───────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<UserListResultDto>> GetUsers(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await adminUserService.SearchUsersAsync(search, page, pageSize, ct);
        return Ok(result);
    }

    // ── GET api/admin/users/stats ─────────────────────────────────────────────
    //
    // Mapped before no id-based route exists to conflict with — this service's admin API has no
    // GET api/admin/users/{id} endpoint (only list/search), so "stats" cannot collide with an id
    // route segment.
    [HttpGet("stats")]
    public async Task<ActionResult<UserStatsDto>> GetStats(CancellationToken ct)
    {
        var stats = await adminUserService.GetStatsAsync(ct);
        return Ok(stats);
    }

    // ── POST api/admin/users ──────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<CreateUserResponseDto>> CreateUser(
        [FromBody] CreateUserRequestDto dto, CancellationToken ct)
    {
        var actor = User.Identity!.Name!;
        var linkBaseUrl = BuildLinkBaseUrl();

        var result = await adminUserService.CreateUserAsync(dto, actor, User.IsInRole("admin"), linkBaseUrl, ct);

        if (result.Status == AdminActionStatus.ValidationFailed)
        {
            return ValidationProblemResult(result.Errors);
        }

        logger.LogInformation("Admin {Actor} created user {UserId} via the admin API", actor, result.Value!.Id);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    // ── POST api/admin/users/{id}/reset-password ──────────────────────────────
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<ResetPasswordResponseDto>> ResetPassword(
        string id, [FromBody] ResetPasswordRequestDto dto, CancellationToken ct)
    {
        var actor = User.Identity!.Name!;
        var linkBaseUrl = BuildLinkBaseUrl();

        var result = await adminUserService.ResetPasswordAsync(id, dto, actor, User.IsInRole("admin"), linkBaseUrl, ct);

        return result.Status switch
        {
            AdminActionStatus.NotFound => UserNotFoundResult(id),
            AdminActionStatus.ValidationFailed => ValidationProblemResult(result.Errors),
            _ => Ok(result.Value),
        };
    }

    // ── POST api/admin/users/{id}/resend-confirmation ─────────────────────────
    [HttpPost("{id}/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(string id, CancellationToken ct)
    {
        var actor = User.Identity!.Name!;
        var linkBaseUrl = BuildLinkBaseUrl();

        var result = await adminUserService.ResendConfirmationAsync(id, actor, User.IsInRole("admin"), linkBaseUrl, ct);

        return result.Status switch
        {
            AdminActionStatus.NotFound => UserNotFoundResult(id),
            AdminActionStatus.ValidationFailed => ValidationProblemResult(result.Errors),
            _ => NoContent(),
        };
    }

    // ── PUT api/admin/users/{id}/roles ────────────────────────────────────────
    [HttpPut("{id}/roles")]
    public async Task<ActionResult<RolesUpdateResponseDto>> UpdateRoles(
        string id, [FromBody] RolesUpdateRequestDto dto, CancellationToken ct)
    {
        var actor = User.Identity!.Name!;

        var result = await adminUserService.UpdateRolesAsync(id, dto, actor, User.IsInRole("admin"), ct);

        return result.Status switch
        {
            AdminActionStatus.NotFound => UserNotFoundResult(id),
            AdminActionStatus.ValidationFailed => ValidationProblemResult(result.Errors),
            _ => Ok(result.Value),
        };
    }

    // ── PUT api/admin/users/{id}/lock ─────────────────────────────────────────
    [HttpPut("{id}/lock")]
    public async Task<ActionResult<LockResponseDto>> SetLock(
        string id, [FromBody] LockRequestDto dto, CancellationToken ct)
    {
        var actor = User.Identity!.Name!;

        var result = await adminUserService.SetLockAsync(id, dto, actor, User.IsInRole("admin"), ct);

        return result.Status switch
        {
            AdminActionStatus.NotFound => UserNotFoundResult(id),
            AdminActionStatus.ValidationFailed => ValidationProblemResult(result.Errors),
            _ => Ok(result.Value),
        };
    }

    // Trusting Request.Host is only safe because AllowedHosts (appsettings.json) is restricted —
    // same convention as Pages/Account/Register/Index.cshtml.cs's identical confirmation-link
    // construction; HostFilteringMiddleware rejects a forged Host header with 400 before this
    // code ever runs.
    private string BuildLinkBaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private NotFoundObjectResult UserNotFoundResult(string id) => NotFound(new ProblemDetails
    {
        Title = "User not found",
        Detail = $"No user with id '{id}' was found.",
        Status = StatusCodes.Status404NotFound,
    });

    private BadRequestObjectResult ValidationProblemResult(IReadOnlyList<string> errors) => BadRequest(new ProblemDetails
    {
        Title = "Request could not be completed",
        Detail = string.Join(" ", errors),
        Status = StatusCodes.Status400BadRequest,
    });
}
