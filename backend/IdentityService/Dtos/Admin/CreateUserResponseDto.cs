namespace IdentityService.Dtos.Admin;

/// <summary>Response body for <c>POST api/admin/users</c>.</summary>
public class CreateUserResponseDto
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public string? Email { get; init; }
    public bool EmailConfirmed { get; init; }
}
