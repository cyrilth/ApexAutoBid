using IdentityService.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
    public static UserManager<ApplicationUser> CreateUserManager(InMemoryUserStore store)
    {
        var identityOptions = new IdentityOptions
        {
            // Matches HostingExtensions.ConfigureServices' Configure<IdentityOptions> (Phase 3
            // Task 14 landmine (a)) so RegisterPageTests exercises the same duplicate-email
            // rejection the real app enforces.
            User = { RequireUniqueEmail = true },
        };

        var userManager = new UserManager<ApplicationUser>(
            store,
            Options.Create(identityOptions),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // AddIdentity<>().AddDefaultTokenProviders() (HostingExtensions) is what registers this
        // in the real app via DI — bypassed by this constructor call, so it's registered by
        // hand here. Needed for GenerateEmailConfirmationTokenAsync/ConfirmEmailAsync (Phase 3
        // Task 14), which Register/Index.cshtml.cs now calls unconditionally on every successful
        // registration. EphemeralDataProtectionProvider is an in-memory-only IDataProtectionProvider
        // built exactly for scenarios like this (no key persistence needed across a single test run).
        userManager.RegisterTokenProvider(
            TokenOptions.DefaultProvider,
            new DataProtectorTokenProvider<ApplicationUser>(
                new EphemeralDataProtectionProvider(),
                Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));

        return userManager;
    }

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
