using System.Collections.Concurrent;
using System.Security.Claims;
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
/// Deliberately does not implement lockout/role/token stores — nothing under test here
/// exercises them, and UserManager/SignInManager degrade gracefully (Supports* flags false)
/// when a store interface isn't implemented.
/// <para>
/// <see cref="IUserEmailStore{TUser}"/> IS implemented (Phase 3 Task 14) — RequireUniqueEmail
/// validation (<c>UserValidator{TUser}.ValidateAsync</c>) calls <c>UserManager.FindByEmailAsync</c>,
/// which throws <see cref="NotSupportedException"/> unless the store supports it — needed for
/// RegisterPageTests' duplicate-email coverage.
/// </para>
/// <para>
/// <see cref="IUserLoginStore{TUser}"/> and <see cref="IUserClaimStore{TUser}"/> ARE implemented
/// (Phase 3 Task 15) — ExternalLoginProvisioningServiceTests exercises
/// <c>UserManager.AddLoginAsync</c>/<c>GetLoginsAsync</c>/<c>AddClaimsAsync</c> for real, the same
/// real-store-over-canned-double rationale as above.
/// </para>
/// </summary>
public sealed class InMemoryUserStore :
    IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>, IUserEmailStore<ApplicationUser>,
    IUserLoginStore<ApplicationUser>, IUserClaimStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _usersByNormalizedName = new();
    private readonly ConcurrentDictionary<string, List<UserLoginInfo>> _loginsByUserId = new();
    private readonly ConcurrentDictionary<string, List<Claim>> _claimsByUserId = new();

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

    // ── IUserEmailStore<ApplicationUser> ──────────────────────────────────────────
    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken ct)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(_usersByNormalizedName.Values.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken ct)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // ── IUserLoginStore<ApplicationUser> ──────────────────────────────────────────
    public Task AddLoginAsync(ApplicationUser user, UserLoginInfo login, CancellationToken ct)
    {
        var logins = _loginsByUserId.GetOrAdd(user.Id, static _ => []);
        logins.Add(login);
        return Task.CompletedTask;
    }

    public Task RemoveLoginAsync(ApplicationUser user, string loginProvider, string providerKey, CancellationToken ct)
    {
        if (_loginsByUserId.TryGetValue(user.Id, out var logins))
        {
            _ = logins.RemoveAll(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
        }

        return Task.CompletedTask;
    }

    public Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult<IList<UserLoginInfo>>(
            _loginsByUserId.TryGetValue(user.Id, out var logins) ? [.. logins] : []);

    public Task<ApplicationUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken ct)
    {
        var userId = _loginsByUserId
            .Where(kvp => kvp.Value.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey))
            .Select(kvp => kvp.Key)
            .FirstOrDefault();

        return Task.FromResult(
            userId is null ? null : _usersByNormalizedName.Values.FirstOrDefault(u => u.Id == userId));
    }

    // ── IUserClaimStore<ApplicationUser> ──────────────────────────────────────────
    public Task<IList<Claim>> GetClaimsAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult<IList<Claim>>(
            _claimsByUserId.TryGetValue(user.Id, out var claims) ? [.. claims] : []);

    public Task AddClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken ct)
    {
        var stored = _claimsByUserId.GetOrAdd(user.Id, static _ => []);
        stored.AddRange(claims);
        return Task.CompletedTask;
    }

    public Task ReplaceClaimAsync(ApplicationUser user, Claim claim, Claim newClaim, CancellationToken ct)
    {
        if (_claimsByUserId.TryGetValue(user.Id, out var stored))
        {
            _ = stored.RemoveAll(c => c.Type == claim.Type && c.Value == claim.Value);
            stored.Add(newClaim);
        }

        return Task.CompletedTask;
    }

    public Task RemoveClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken ct)
    {
        if (_claimsByUserId.TryGetValue(user.Id, out var stored))
        {
            foreach (var claim in claims)
            {
                _ = stored.RemoveAll(c => c.Type == claim.Type && c.Value == claim.Value);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IList<ApplicationUser>> GetUsersForClaimAsync(Claim claim, CancellationToken ct)
    {
        var userIds = _claimsByUserId
            .Where(kvp => kvp.Value.Any(c => c.Type == claim.Type && c.Value == claim.Value))
            .Select(kvp => kvp.Key)
            .ToHashSet();

        return Task.FromResult<IList<ApplicationUser>>(
            [.. _usersByNormalizedName.Values.Where(u => userIds.Contains(u.Id))]);
    }
}
