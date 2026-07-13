namespace IdentityService.Dtos.Admin;

/// <summary>Paged response for <c>GET api/admin/users</c>.</summary>
public class UserListResultDto
{
    public required IReadOnlyList<UserListItemDto> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
