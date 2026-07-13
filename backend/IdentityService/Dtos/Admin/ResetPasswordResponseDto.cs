namespace IdentityService.Dtos.Admin;

/// <summary>Response body for <c>POST api/admin/users/{id}/reset-password</c>.</summary>
public class ResetPasswordResponseDto
{
    public bool LinkSent { get; init; }

    /// <summary>
    /// Echoes the new temporary password back to the calling admin exactly once — only present
    /// when <see cref="ResetPasswordRequestDto.SendResetLink"/> was <see langword="false"/>. This
    /// is the one legitimate place plaintext password material crosses the API boundary; it is
    /// never written to an <see cref="Models.AuditEntry"/> and never logged (Requirements.md
    /// §13.3/§13.5).
    /// </summary>
    public string? TemporaryPassword { get; init; }
}
