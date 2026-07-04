using System.Collections.Concurrent;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.UnitTests;

/// <summary>
/// Minimal in-memory <see cref="IUserStore{TUser}"/>/<see cref="IUserPasswordStore{TUser}"/>
/// fake — no database, no Postgres. Backs a REAL <see cref="UserManager{TUser}"/> (see
/// <see cref="TestUserManagerFactory"/>) rather than substituting UserManager itself, so tests
/// exercise ASP.NET Core Identity's actual validation pipeline (in particular
/// <see cref="UserValidator{TUser}"/>'s duplicate-username check that Task 10.4 covers) instead
/// of a canned double that would just prove the PageModel copies whatever result it's handed.
/// Deliberately does not implement lockout/email/role/token stores — nothing under test here
/// exercises them, and UserManager/SignInManager degrade gracefully (Supports* flags false)
/// when a store interface isn't implemented.
/// </summary>
public sealed class InMemoryUserStore : IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _usersByNormalizedName = new();

    public void Dispose() { }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!_usersByNormalizedName.TryAdd(user.NormalizedUserName!, user))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = $"Username '{user.UserName}' is already taken.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        _usersByNormalizedName[user.NormalizedUserName!] = user;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        _usersByNormalizedName.TryRemove(user.NormalizedUserName!, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        Task.FromResult(_usersByNormalizedName.Values.FirstOrDefault(u => u.Id == userId));

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
        Task.FromResult(_usersByNormalizedName.GetValueOrDefault(normalizedUserName));

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken ct)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash is not null);
}
