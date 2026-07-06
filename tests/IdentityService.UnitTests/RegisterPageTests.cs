using System.Security.Claims;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using IdentityService.Pages.Account.Register;
using IdentityService.Services;
using RegisterPage = IdentityService.Pages.Account.Register.Index;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly IEmailSender<ApplicationUser> _emailSender = Substitute.For<IEmailSender<ApplicationUser>>();
    private readonly IAuthenticationSchemeProvider _schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
    private readonly ITurnstileValidator _turnstileValidator = Substitute.For<ITurnstileValidator>();

    public RegisterPageTests()
    {
        _userManager = TestIdentityFactory.CreateUserManager(_store);
        _signInManager = TestIdentityFactory.CreateSignInManagerSubstitute(_userManager);

        // No pending OIDC authorize request — a direct visit to the register page.
        _interaction.GetAuthorizationContextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AuthorizationRequest?)null);

        // Phase 3 Task 15 — no Google (or any other) scheme registered in these tests; the page
        // must still render/redisplay correctly with an empty external-provider list, not NRE on
        // an unconfigured NSubstitute Task<T> default.
        _schemeProvider.GetAllSchemesAsync().Returns(Enumerable.Empty<AuthenticationScheme>());

        // Phase 3 Task 16.1 — default to "passes" so every PRE-EXISTING test (which supplies a
        // TurnstileResponse token via BuildPageModel below) keeps exercising the same behavior
        // it always did; TurnstileValidatorTests.cs covers the validator's own logic in
        // isolation, and the two new tests below override this per-test to cover the gating
        // logic specifically (missing token, failed validation).
        _turnstileValidator.ValidateAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private RegisterPage BuildPageModel()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var turnstileOptions = Options.Create(new TurnstileOptions
        {
            SiteKey = "1x00000000000000000000AA",
            SecretKey = "1x0000000000000000000000000000000AA",
        });
        return new RegisterPage(_userManager, _signInManager, _interaction, _emailSender, _schemeProvider, _turnstileValidator, turnstileOptions, NullLogger<RegisterPage>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
            Url = Substitute.For<IUrlHelper>(),
            // Cloudflare's widget always posts a non-empty token when it succeeds — every
            // pre-existing test represents that "happy path" default; the two new
            // Turnstile-specific tests below override this to exercise the missing/rejected
            // cases instead.
            TurnstileResponse = "test-turnstile-token",
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

        // Signed in immediately post-registration — unconfirmed accounts can still log in and
        // browse (Requirements.md §3.4); only mutating actions require email_verified (Task
        // 14.4, enforced by AuctionsController.cs, not this page). Verified as a call, not a
        // real cookie write.
        await _signInManager.Received(1).SignInAsync(created, false, Arg.Any<string?>());

        // Phase 3 Task 14.1 — a confirmation email was queued for the new user (not a canned
        // no-op skip). The link content itself is exercised live against real Mailpit/Postgres
        // infra, not asserted here (see Task 14's report).
        await _emailSender.Received(1).SendConfirmationLinkAsync(
            created, "newuser@apexautobid.local", Arg.Any<string>());
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

    // ── 14 landmine (a) — RequireUniqueEmail rejects a second account with the same email ──
    [Fact]
    public async Task OnPostAsync_DuplicateEmail_ReturnsModelStateErrorWithoutCreatingSecondUser()
    {
        var existing = new ApplicationUser { UserName = "emailowner", Email = "shared@apexautobid.local" };
        var seedResult = await _userManager.CreateAsync(existing, "Pass123$");
        Assert.True(seedResult.Succeeded, string.Join(", ", seedResult.Errors.Select(e => e.Code)));

        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel
        {
            Username = "differentusername",
            Email = "shared@apexautobid.local",
            Password = "Pass123$",
            ConfirmPassword = "Pass123$",
            Button = "register",
        };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        // Options.User.RequireUniqueEmail = true (HostingExtensions, Phase 3 Task 14 landmine
        // (a)) — UserValidator surfaces this the same way as a duplicate username: a
        // ModelState error and a re-rendered page, never a 500.
        Assert.IsType<PageResult>(result);
        Assert.False(pageModel.ModelState.IsValid);
        var error = Assert.Single(pageModel.ModelState[string.Empty]!.Errors);
        Assert.Equal("Email 'shared@apexautobid.local' is already taken.", error.ErrorMessage);

        // No second user was created, no email was sent, and the sign-in step never ran.
        var stillOnlyOne = await _userManager.FindByNameAsync("differentusername");
        Assert.Null(stillOnlyOne);
        await _emailSender.DidNotReceive().SendConfirmationLinkAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    // ── 16.1 — missing Turnstile token rejects WITHOUT calling the validator ─────
    [Fact]
    public async Task OnPostAsync_MissingTurnstileToken_RejectsWithoutCallingValidator()
    {
        var pageModel = BuildPageModel();
        pageModel.TurnstileResponse = null; // widget didn't run / JS disabled / direct POST
        pageModel.Input = new InputModel
        {
            Username = "turnstileuser1",
            Email = "turnstileuser1@apexautobid.local",
            Password = "Pass123$",
            ConfirmPassword = "Pass123$",
            Button = "register",
        };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(pageModel.ModelState.IsValid);
        var error = Assert.Single(pageModel.ModelState[string.Empty]!.Errors);
        Assert.Equal("Please complete the verification challenge.", error.ErrorMessage);

        // The whole point of rejecting early: never burn a siteverify call on an obviously
        // incomplete submission.
        await _turnstileValidator.DidNotReceive().ValidateAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        var noUserCreated = await _userManager.FindByNameAsync("turnstileuser1");
        Assert.Null(noUserCreated);
    }

    // ── 16.1 — a token that fails siteverify rejects the same way ────────────────
    [Fact]
    public async Task OnPostAsync_TurnstileValidationFails_RejectsWithoutCreatingUser()
    {
        _turnstileValidator.ValidateAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var pageModel = BuildPageModel();
        pageModel.Input = new InputModel
        {
            Username = "turnstileuser2",
            Email = "turnstileuser2@apexautobid.local",
            Password = "Pass123$",
            ConfirmPassword = "Pass123$",
            Button = "register",
        };

        var result = await pageModel.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(pageModel.ModelState.IsValid);
        var error = Assert.Single(pageModel.ModelState[string.Empty]!.Errors);
        Assert.Equal("Verification challenge failed. Please try again.", error.ErrorMessage);

        var noUserCreated = await _userManager.FindByNameAsync("turnstileuser2");
        Assert.Null(noUserCreated);
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }
}
