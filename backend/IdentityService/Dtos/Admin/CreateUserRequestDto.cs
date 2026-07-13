using System.ComponentModel.DataAnnotations;

namespace IdentityService.Dtos.Admin;

/// <summary>Request body for <c>POST api/admin/users</c>.</summary>
public class CreateUserRequestDto
{
    [Required]
    [MaxLength(256)]
    public required string UserName { get; init; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the account is created with its email already confirmed and
    /// NO confirmation email is sent — an admin-vouched-for account. When <see langword="false"/>
    /// (the default), a confirmation email is sent exactly like self-service registration
    /// (<c>Pages/Account/Register/Index.cshtml.cs</c>).
    /// </summary>
    public bool PreConfirmed { get; init; }
}
