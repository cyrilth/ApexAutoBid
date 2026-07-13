using System.ComponentModel.DataAnnotations;

namespace IdentityService.Dtos.Admin;

/// <summary>Request body for <c>POST api/admin/users/{id}/reset-password</c>.</summary>
public class ResetPasswordRequestDto
{
    /// <summary>
    /// <see langword="true"/>: generate a password-reset token and email the user a reset link
    /// (<see cref="NewPassword"/> is ignored). <see langword="false"/>: set
    /// <see cref="NewPassword"/> directly as the account's new (temporary) password —
    /// <see cref="NewPassword"/> is then required.
    /// </summary>
    public bool SendResetLink { get; init; }

    [MinLength(6)]
    public string? NewPassword { get; init; }
}
