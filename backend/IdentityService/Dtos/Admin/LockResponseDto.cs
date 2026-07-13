namespace IdentityService.Dtos.Admin;

/// <summary>Response body for <c>PUT api/admin/users/{id}/lock</c>.</summary>
public class LockResponseDto
{
    public required string Id { get; init; }
    public bool LockedOut { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
}
