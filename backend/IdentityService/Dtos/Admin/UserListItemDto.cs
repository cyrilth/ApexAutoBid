namespace IdentityService.Dtos.Admin;

/// <summary>
/// One row of <c>GET api/admin/users</c>'s paged result. API-boundary DTO — never exposes
/// <see cref="Models.ApplicationUser"/> (the Identity entity) directly.
/// </summary>
public class UserListItemDto
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public string? Email { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool LockedOut { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}
