namespace IdentityService.Dtos.Admin;

/// <summary>Response body for <c>PUT api/admin/users/{id}/roles</c> — the user's roles after the update.</summary>
public class RolesUpdateResponseDto
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}
