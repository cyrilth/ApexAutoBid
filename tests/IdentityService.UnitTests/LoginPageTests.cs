using System.Security.Claims;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Stores;
using IdentityService.Models;
using IdentityService.Pages.Account.Login;
using LoginPage = IdentityService.Pages.Account.Login.Index;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for the Login PageModel (Phase 3 Task 10.1/10.2).
/// <para>
/// <b>Interpretation note (checkbox wording vs. actual surface):</b> IdentityService is the
/// Duende Razor Pages host, not a JSON token API — the login page never returns a token
/// itself (that's minted by Duende's <c>/connect/token</c> endpoint, already verified live
/// with real tokens in Tasks 3/5/7). "Valid credentials returns token" is read here as "valid
/// credentials produce the page's successful-sign-in outcome" (a redirect, not a 200 with a
/// token body). Likewise "invalid credentials returns 401" is read as "the page's actual
/// rejection outcome" — a re-rendered <see cref="PageResult"/> (HTTP 200) carrying the
/// invalid-credentials <c>ModelState</c> error, since this is a browser form post, not an API
/// call that could return a 401 status code.
/// </para>
/// </summary>
public class LoginPageTests
{
    private readonly InMemoryUserStore _store = new();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction = Substitute.For<IIdentityServerInteractionService>();
    private readonly IAuthenticationSchemeProvider _schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
    private readonly IIdentityProviderStore _identityProviderStore = Substitute.For<IIdentityProviderStore>();
    private readonly IEventService _events = Substitute.For<IEventService>();

    public LoginPageTests()
    {
        _userManager = TestIdentityFactory.CreateUserManager(_store);
        _signInManager = TestIdentityFactory.CreateSignInManagerSubstitute(_userManager);

        // No pending OIDC authorize request for any of these tests — a direct visit to the
        // login page, not a redirect from /connect/authorize.
        _interaction.GetAuthorizationContextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AuthorizationRequest?)null);

        // BuildModelAsync (run on the failure path) enumerates these — empty is a valid,
        // "no external providers configured" response.
        _schemeProvider.GetAllSchemesAsync().Returns([]);
        _identityProviderStore.GetAllSchemeNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<IdentityProviderName>());
    }

    private LoginPage BuildPageModel()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var pageModel = new LoginPage(_interaction, _schemeProvider, _identityProviderStore, _events, _userManager, _signInManager)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
            Url = Substitute.For<IUrlHelper>(),
        };
        return pageModel;
    }

    // ── 10.1  Login — valid credentials returns token ────────────────────────────
    // (see class remarks: "token" here means the page's successful-sign-in redirect outcome —
    // the actual JWT is minted by IdentityServer's token endpoint, not this page.)
    [Fact]
    public async Task OnPostAsync_ValidCredentials_SignsInAndRedirectsHome()
    {
        var user = new ApplicationUser { UserName = "bob", Email = "bob@apexautobid.local", EmailConfirmed = true };
        var seedResult = await _userManager.CreateAsync(user, "Pass123$");
        Assert.True(seedResult.Succeeded, string.Join(", ", seedResult.Errors.Select(e => e.Description)));

        _signInManager.PasswordSignInAsync("bob", "Pass123$", isPersistent: false, lockoutOnFailure: true)
            .Returns(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel { Username = "bob", Password = "Pass123$", Button = "login" };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("~/", redirect.Url);
        Assert.True(pageModel.ModelState.IsValid);
    }

    // ── 10.2  Login — invalid credentials returns 401 ────────────────────────────
    // (see class remarks: the page's real outcome is a re-rendered Page with a ModelState
    // error, not an HTTP 401 — this is a browser form post, not a token-endpoint call.)
    [Fact]
    public async Task OnPostAsync_InvalidCredentials_ReturnsPageWithInvalidCredentialsError()
    {
        _signInManager.PasswordSignInAsync("bob", "wrong-password", isPersistent: false, lockoutOnFailure: true)
            .Returns(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel { Username = "bob", Password = "wrong-password", Button = "login" };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(pageModel.ModelState.IsValid);
        var error = Assert.Single(pageModel.ModelState[string.Empty]!.Errors);
        Assert.Equal(LoginOptions.InvalidCredentialsErrorMessage, error.ErrorMessage);
    }
}
