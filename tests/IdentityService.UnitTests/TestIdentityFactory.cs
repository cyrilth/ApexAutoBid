using IdentityService.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Builds the ASP.NET Core Identity managers the Login/Register PageModels depend on, for use
/// without a database.
/// <para>
/// <see cref="UserManager{TUser}"/> is a REAL instance backed by <see cref="InMemoryUserStore"/>
/// (see that class's remarks) — this exercises actual Identity validation logic (duplicate
/// username, password policy) rather than a canned double.
/// </para>
/// <para>
/// <see cref="SignInManager{TUser}"/> is instead an NSubstitute double: its real
/// implementation ultimately calls <c>HttpContext.SignInAsync</c>, which needs a fully wired
/// <c>IAuthenticationService</c> in <c>HttpContext.RequestServices</c> to write an auth cookie —
/// standing up that plumbing is integration-test territory (Task 11), not this unit test's
/// concern. The PageModels only need <c>PasswordSignInAsync</c>'s returned <see cref="SignInResult"/>
/// and a callable (no-op is fine) <c>SignInAsync</c>, both of which NSubstitute gives directly.
/// </para>
/// </summary>
internal static class TestIdentityFactory
{
    public static UserManager<ApplicationUser> CreateUserManager(InMemoryUserStore store) =>
        new(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

    public static SignInManager<ApplicationUser> CreateSignInManagerSubstitute(UserManager<ApplicationUser> userManager) =>
        Substitute.For<SignInManager<ApplicationUser>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ApplicationUser>>());
}
