namespace IdentityService.Dtos.Admin;

/// <summary>
/// Request body for <c>PUT api/admin/users/{id}/roles</c> — the FULL desired role set for the
/// user. Roles listed here that the user does not currently have are added; roles the user
/// currently has that are absent here are removed.
/// </summary>
public class RolesUpdateRequestDto
{
    public required IReadOnlyList<string> Roles { get; init; }
}
