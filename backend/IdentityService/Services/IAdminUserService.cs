using IdentityService.Dtos.Admin;

namespace IdentityService.Services;

/// <summary>
/// Admin user-management operations backing <see cref="Controllers.AdminUsersController"/>
/// (Phase 11 Task 2 / Requirements.md §10.1). Extracted behind an interface — like
/// <see cref="ExternalLoginProvisioningService"/> before it — so the DECISION logic (search,
/// creation, role/lockout changes, auditing) is unit-testable without a live HTTP pipeline.
/// </summary>
public interface IAdminUserService
{
    Task<UserListResultDto> SearchUsersAsync(string? search, int page, int pageSize, CancellationToken ct);

    Task<UserStatsDto> GetStatsAsync(CancellationToken ct);

    Task<AdminActionResult<CreateUserResponseDto>> CreateUserAsync(
        CreateUserRequestDto dto, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct);

    Task<AdminActionResult<ResetPasswordResponseDto>> ResetPasswordAsync(
        string userId, ResetPasswordRequestDto dto, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct);

    Task<AdminActionResult<object?>> ResendConfirmationAsync(
        string userId, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct);

    Task<AdminActionResult<RolesUpdateResponseDto>> UpdateRolesAsync(
        string userId, RolesUpdateRequestDto dto, string actor, bool actorIsAdmin, CancellationToken ct);

    Task<AdminActionResult<LockResponseDto>> SetLockAsync(
        string userId, LockRequestDto dto, string actor, bool actorIsAdmin, CancellationToken ct);
}
