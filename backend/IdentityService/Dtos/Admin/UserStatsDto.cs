namespace IdentityService.Dtos.Admin;

/// <summary>Response body for <c>GET api/admin/users/stats</c>.</summary>
public class UserStatsDto
{
    public int Total { get; init; }
    public int Confirmed { get; init; }
    public int Locked { get; init; }
}
