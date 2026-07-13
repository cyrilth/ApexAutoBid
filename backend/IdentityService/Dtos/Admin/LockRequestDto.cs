namespace IdentityService.Dtos.Admin;

/// <summary>Request body for <c>PUT api/admin/users/{id}/lock</c>.</summary>
public class LockRequestDto
{
    /// <summary><see langword="true"/> locks the account; <see langword="false"/> unlocks it.</summary>
    public bool Locked { get; init; }

    /// <summary>
    /// Optional explicit lockout end (UTC) when locking; ignored when unlocking. When locking
    /// without an explicit value, defaults to 100 years out (an effectively indefinite lockout,
    /// mirroring the framework's own "AllowedForNewUsers"/lockout defaults already configured in
    /// <c>HostingExtensions</c>).
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; init; }
}
