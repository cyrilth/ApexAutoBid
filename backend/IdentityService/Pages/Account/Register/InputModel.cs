using System.ComponentModel.DataAnnotations;

namespace IdentityService.Pages.Account.Register;

public class InputModel
{
    [Required]
    [Display(Name = "Username")]
    public string? Username { get; set; }

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    // Password strength itself is enforced by ASP.NET Core Identity's default
    // UserManager<T> password validators (RequireDigit/RequireLowercase/RequireUppercase/
    // RequireNonAlphanumeric/RequiredLength=6) — Requirements.md doesn't specify custom
    // password rules, so no bespoke [MinLength]/regex is added here; UserManager.CreateAsync's
    // IdentityResult.Errors are surfaced to ModelState in Index.cshtml.cs.
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    public string? ConfirmPassword { get; set; }

    public string? ReturnUrl { get; set; }
    public string? Button { get; set; }
}
