using System.Security.Claims;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using IdentityService.Pages.Account.Register;
using RegisterPage = IdentityService.Pages.Account.Register.Index;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for the Register PageModel (Phase 3 Task 10.3/10.4). Uses a REAL
/// <see cref="UserManager{TUser}"/> backed by <see cref="InMemoryUserStore"/> (see that
/// class's remarks) so <c>UserManager.CreateAsync</c>'s actual validation pipeline — including
/// the duplicate-username check Task 10.4 covers — runs for real, not a canned double.
/// <see cref="SignInManager{TUser}"/> is an NSubstitute double (see
/// <see cref="TestIdentityFactory"/>): the post-registration <c>SignInAsync</c> call is
/// verified as having happened, not its real cookie-writing side effect, which needs
/// integration-test-level HttpContext plumbing (Task 11).
/// </summary>
public class RegisterPageTests
{
    private readonly InMemoryUserStore _store = new();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction = Substitute.For<IIdentityServerInteractionService>();

    public RegisterPageTests()
    {
        _userManager = TestIdentityFactory.CreateUserManager(_store);
        _signInManager = TestIdentityFactory.CreateSignInManagerSubstitute(_userManager);

        // No pending OIDC authorize request — a direct visit to the register page.
        _interaction.GetAuthorizationContextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AuthorizationRequest?)null);
    }

    private RegisterPage BuildPageModel()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        return new RegisterPage(_userManager, _signInManager, _interaction, NullLogger<RegisterPage>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
            Url = Substitute.For<IUrlHelper>(),
        };
    }

    // ── 10.3  Register — valid data creates user ─────────────────────────────────
    [Fact]
    public async Task OnPostAsync_ValidData_PersistsUserAndSignsIn()
    {
        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel
        {
            Username = "newuser",
            Email = "newuser@apexautobid.local",
            Password = "Pass123$",
            ConfirmPassword = "Pass123$",
            Button = "register",
        };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("~/", redirect.Url);
        Assert.True(pageModel.ModelState.IsValid);

        // The behavior that actually matters for "creates user": a real row exists in the
        // (fake, in-memory) user store afterward, created via UserManager — not just that the
        // PageModel returned a success-shaped result.
        var created = await _userManager.FindByNameAsync("newuser");
        Assert.NotNull(created);
        Assert.Equal("newuser@apexautobid.local", created!.Email);

        // Signed in immediately post-registration (Task 4's interim design, ahead of email
        // verification landing in Task 14) — verified as a call, not a real cookie write.
        await _signInManager.Received(1).SignInAsync(created, false, Arg.Any<string?>());
    }

    // ── 10.4  Register — duplicate username returns error ────────────────────────
    [Fact]
    public async Task OnPostAsync_DuplicateUsername_ReturnsModelStateErrorWithoutCreatingSecondUser()
    {
        var existing = new ApplicationUser { UserName = "dupeuser", Email = "dupe1@apexautobid.local" };
        var seedResult = await _userManager.CreateAsync(existing, "Pass123$");
        Assert.True(seedResult.Succeeded, string.Join(", ", seedResult.Errors.Select(e => e.Description)));

        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel
        {
            Username = "dupeuser",
            Email = "dupe2@apexautobid.local",
            Password = "Pass123$",
            ConfirmPassword = "Pass123$",
            Button = "register",
        };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        // Never a 500 — UserManager.CreateAsync's IdentityResult.Errors are surfaced as a
        // ModelState error and the page is re-rendered (see Index.cshtml.cs's own comment).
        Assert.IsType<PageResult>(result);
        Assert.False(pageModel.ModelState.IsValid);
        var error = Assert.Single(pageModel.ModelState[string.Empty]!.Errors);
        Assert.Equal("Username 'dupeuser' is already taken.", error.ErrorMessage);

        // No second user was created, and the sign-in step never ran.
        var stillOnlyOne = await _userManager.FindByNameAsync("dupeuser");
        Assert.Equal("dupe1@apexautobid.local", stillOnlyOne!.Email);
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }
}
