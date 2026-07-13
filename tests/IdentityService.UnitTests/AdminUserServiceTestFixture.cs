using IdentityService.Data;
using IdentityService.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Builds a REAL <see cref="ApplicationDbContext"/> (backed by an in-memory SQLite database,
/// kept alive for the fixture's lifetime via an open <see cref="SqliteConnection"/>) plus REAL
/// <see cref="UserManager{TUser}"/>/<see cref="RoleManager{TRole}"/> instances backed by the
/// stable, public 3-generic-parameter <see cref="UserStore{TUser,TRole,TContext}"/>/
/// <see cref="RoleStore{TRole,TContext}"/> constructors — the same store types
/// <c>AddEntityFrameworkStores&lt;ApplicationDbContext&gt;()</c> wires up in the real app.
/// <para>
/// Unlike <see cref="InMemoryUserStore"/> (used by the Login/Register/external-login tests),
/// <see cref="AdminUserService"/> needs a REAL relational <see cref="DbContext"/> — it writes
/// <see cref="AuditEntry"/> rows via <c>ApplicationDbContext.SaveChangesAsync</c> and wraps
/// several operations in an explicit <c>Database.BeginTransactionAsync</c>, neither of which a
/// hand-written fake store can exercise. SQLite in-memory (not EF Core's InMemory provider) is
/// used specifically because <c>Database.BeginTransactionAsync</c> is a relational-only EF Core
/// API — EF Core's InMemory provider throws <see cref="InvalidOperationException"/> for it.
/// </para>
/// </summary>
public sealed class AdminUserServiceTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext DbContext { get; }

    public UserManager<ApplicationUser> UserManager { get; }

    public RoleManager<IdentityRole> RoleManager { get; }

    public AdminUserServiceTestFixture()
    {
        // The in-memory SQLite database only exists while this connection stays open — closing
        // it (Dispose) tears the database down, giving each test class its own isolated fixture.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        DbContext = new ApplicationDbContext(options);
        DbContext.Database.EnsureCreated();

        var identityOptions = new IdentityOptions { User = { RequireUniqueEmail = true } };

        var userStore = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(DbContext);
        UserManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(identityOptions),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // Registered by hand — AddIdentity().AddDefaultTokenProviders() does this via DI in the
        // real app (bypassed here). Needed for GenerateEmailConfirmationTokenAsync/
        // GeneratePasswordResetTokenAsync/ResetPasswordAsync, all exercised by AdminUserService.
        UserManager.RegisterTokenProvider(
            TokenOptions.DefaultProvider,
            new DataProtectorTokenProvider<ApplicationUser>(
                new EphemeralDataProtectionProvider(),
                Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));

        var roleStore = new RoleStore<IdentityRole, ApplicationDbContext>(DbContext);
        RoleManager = new RoleManager<IdentityRole>(
            roleStore,
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        _connection.Dispose();
    }
}
