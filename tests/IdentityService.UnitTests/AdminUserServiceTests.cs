using IdentityService.Dtos.Admin;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for <see cref="AdminUserService"/> (Phase 11 Task 2) — happy paths for each admin
/// user-management operation, and that every mutating operation writes an
/// <see cref="AuditEntry"/> row (Requirements.md §13.3). Uses
/// <see cref="AdminUserServiceTestFixture"/>'s real (SQLite in-memory) UserManager/RoleManager/
/// ApplicationDbContext — see that class's remarks for why a real relational DbContext is
/// needed here rather than <see cref="InMemoryUserStore"/>.
/// <para>
/// 403-for-non-admin-caller coverage (the controller-level "AdminOnly" policy gate) lives in
/// <c>IdentityService.IntegrationTests/AdminUsersAuthorizationTests.cs</c> instead — it needs the
/// real JWT bearer authentication/authorization pipeline (a real, role-bearing access token),
/// which is integration-test, not unit-test, territory.
/// </para>
/// </summary>
public class AdminUserServiceTests : IDisposable
{
    private readonly AdminUserServiceTestFixture _fixture = new();
    private readonly IEmailSender<ApplicationUser> _emailSender = Substitute.For<IEmailSender<ApplicationUser>>();
    private readonly AdminUserService _sut;

    public AdminUserServiceTests()
    {
        _sut = new AdminUserService(
            _fixture.UserManager,
            _fixture.RoleManager,
            _fixture.DbContext,
            _emailSender,
            NullLogger<AdminUserService>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<ApplicationUser> SeedUserAsync(string userName, string email, bool emailConfirmed = true)
    {
        var user = new ApplicationUser { UserName = userName, Email = email, EmailConfirmed = emailConfirmed };
        var result = await _fixture.UserManager.CreateAsync(user, "Seeded123$");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        return user;
    }

    // ── CreateUserAsync — happy path + audit ─────────────────────────────────────
    [Fact]
    public async Task CreateUserAsync_ValidRequest_CreatesUserAndWritesAuditEntry()
    {
        var dto = new CreateUserRequestDto
        {
            UserName = "newadminuser",
            Email = "newadminuser@apexautobid.local",
            Password = "Pass123$",
            PreConfirmed = true,
        };

        var result = await _sut.CreateUserAsync(dto, "admin", actorIsAdmin: true, "https://localhost:5001", TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.Equal("newadminuser", result.Value!.UserName);
        Assert.True(result.Value.EmailConfirmed);

        var stored = await _fixture.UserManager.FindByNameAsync("newadminuser");
        Assert.NotNull(stored);

        var audit = Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminUserCreated");
        Assert.Equal("admin", audit.Actor);
        Assert.True(audit.ActorIsAdmin);
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(stored!.Id, audit.EntityId);
        Assert.DoesNotContain("newadminuser@apexautobid.local", audit.Data);

        // PreConfirmed=true — no confirmation email should have been queued.
        await _emailSender.DidNotReceive().SendConfirmationLinkAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CreateUserAsync_NotPreConfirmed_SendsConfirmationEmail()
    {
        var dto = new CreateUserRequestDto
        {
            UserName = "unconfirmeduser",
            Email = "unconfirmeduser@apexautobid.local",
            Password = "Pass123$",
            PreConfirmed = false,
        };

        var result = await _sut.CreateUserAsync(dto, "admin", actorIsAdmin: true, "https://localhost:5001", TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.False(result.Value!.EmailConfirmed);

        await _emailSender.Received(1).SendConfirmationLinkAsync(
            Arg.Any<ApplicationUser>(), "unconfirmeduser@apexautobid.local", Arg.Any<string>());
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateUserName_ReturnsValidationFailedAndWritesNoAuditEntry()
    {
        await SeedUserAsync("dupeuser", "dupeuser@apexautobid.local");

        var dto = new CreateUserRequestDto
        {
            UserName = "dupeuser",
            Email = "different@apexautobid.local",
            Password = "Pass123$",
            PreConfirmed = true,
        };

        var result = await _sut.CreateUserAsync(dto, "admin", actorIsAdmin: true, "https://localhost:5001", TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.ValidationFailed, result.Status);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(_fixture.DbContext.AuditEntries);
    }

    // ── SetLockAsync — happy path + audit ────────────────────────────────────────
    [Fact]
    public async Task SetLockAsync_Locked_LocksUserAndWritesAuditEntry()
    {
        var user = await SeedUserAsync("tolock", "tolock@apexautobid.local");

        var result = await _sut.SetLockAsync(
            user.Id, new LockRequestDto { Locked = true }, "admin", actorIsAdmin: true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.True(result.Value!.LockedOut);
        Assert.NotNull(result.Value.LockoutEnd);

        Assert.True(await _fixture.UserManager.IsLockedOutAsync(user));

        var audit = Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminUserLocked");
        Assert.Equal(user.Id, audit.EntityId);
    }

    [Fact]
    public async Task SetLockAsync_Unlocked_UnlocksUserAndWritesAuditEntry()
    {
        var user = await SeedUserAsync("tounlock", "tounlock@apexautobid.local");
        _ = await _sut.SetLockAsync(user.Id, new LockRequestDto { Locked = true }, "admin", true, TestContext.Current.CancellationToken);

        var result = await _sut.SetLockAsync(
            user.Id, new LockRequestDto { Locked = false }, "admin", actorIsAdmin: true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.False(result.Value!.LockedOut);
        Assert.Null(result.Value.LockoutEnd);

        Assert.False(await _fixture.UserManager.IsLockedOutAsync(user));
        Assert.Contains(_fixture.DbContext.AuditEntries, a => a.Action == "AdminUserUnlocked" && a.EntityId == user.Id);
    }

    [Fact]
    public async Task SetLockAsync_UnknownUser_ReturnsNotFound()
    {
        var result = await _sut.SetLockAsync(
            "does-not-exist", new LockRequestDto { Locked = true }, "admin", true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.NotFound, result.Status);
        Assert.Empty(_fixture.DbContext.AuditEntries);
    }

    // ── UpdateRolesAsync — happy path + audit ────────────────────────────────────
    [Fact]
    public async Task UpdateRolesAsync_GrantAdminRole_AssignsRoleAndWritesAuditEntry()
    {
        _ = await _fixture.RoleManager.CreateAsync(new IdentityRole("admin"));
        var user = await SeedUserAsync("promoteme", "promoteme@apexautobid.local");

        var result = await _sut.UpdateRolesAsync(
            user.Id, new RolesUpdateRequestDto { Roles = ["admin"] }, "admin", actorIsAdmin: true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.Contains("admin", result.Value!.Roles);
        Assert.True(await _fixture.UserManager.IsInRoleAsync(user, "admin"));

        var audit = Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminUserRolesChanged");
        Assert.Equal(user.Id, audit.EntityId);
        Assert.Contains("admin", audit.Data);
    }

    [Fact]
    public async Task UpdateRolesAsync_RemoveRole_RemovesRoleAndWritesAuditEntry()
    {
        _ = await _fixture.RoleManager.CreateAsync(new IdentityRole("admin"));
        var user = await SeedUserAsync("demoteme", "demoteme@apexautobid.local");
        _ = await _fixture.UserManager.AddToRoleAsync(user, "admin");

        var result = await _sut.UpdateRolesAsync(
            user.Id, new RolesUpdateRequestDto { Roles = [] }, "admin", actorIsAdmin: true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.DoesNotContain("admin", result.Value!.Roles);
        Assert.False(await _fixture.UserManager.IsInRoleAsync(user, "admin"));

        Assert.Contains(_fixture.DbContext.AuditEntries, a => a.Action == "AdminUserRolesChanged" && a.EntityId == user.Id);
    }

    [Fact]
    public async Task UpdateRolesAsync_UnknownRole_ReturnsValidationFailedAndWritesNoAuditEntry()
    {
        var user = await SeedUserAsync("badrole", "badrole@apexautobid.local");

        var result = await _sut.UpdateRolesAsync(
            user.Id, new RolesUpdateRequestDto { Roles = ["does-not-exist"] }, "admin", true, TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.ValidationFailed, result.Status);
        Assert.Empty(_fixture.DbContext.AuditEntries);
    }

    // ── ResetPasswordAsync — temporary password + audit ──────────────────────────
    [Fact]
    public async Task ResetPasswordAsync_TemporaryPassword_SetsPasswordAndWritesAuditEntry()
    {
        var user = await SeedUserAsync("resetme", "resetme@apexautobid.local");

        var result = await _sut.ResetPasswordAsync(
            user.Id,
            new ResetPasswordRequestDto { SendResetLink = false, NewPassword = "NewPass123$" },
            "admin",
            actorIsAdmin: true,
            "https://localhost:5001",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.False(result.Value!.LinkSent);
        Assert.Equal("NewPass123$", result.Value.TemporaryPassword);

        Assert.True(await _fixture.UserManager.CheckPasswordAsync(user, "NewPass123$"));

        var audit = Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminPasswordReset");
        Assert.Equal(user.Id, audit.EntityId);
        // The new password must never appear in the audit trail.
        Assert.DoesNotContain("NewPass123$", audit.Data);
    }

    [Fact]
    public async Task ResetPasswordAsync_SendResetLink_SendsEmailAndWritesAuditEntryWithoutPassword()
    {
        var user = await SeedUserAsync("linkme", "linkme@apexautobid.local");

        var result = await _sut.ResetPasswordAsync(
            user.Id,
            new ResetPasswordRequestDto { SendResetLink = true },
            "admin",
            actorIsAdmin: true,
            "https://localhost:5001",
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);
        Assert.True(result.Value!.LinkSent);
        Assert.Null(result.Value.TemporaryPassword);

        await _emailSender.Received(1).SendPasswordResetLinkAsync(
            Arg.Any<ApplicationUser>(), "linkme@apexautobid.local", Arg.Any<string>());

        Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminPasswordResetLinkSent");
    }

    // ── ResendConfirmationAsync — happy path + audit ─────────────────────────────
    [Fact]
    public async Task ResendConfirmationAsync_UnconfirmedUser_SendsEmailAndWritesAuditEntry()
    {
        var user = await SeedUserAsync("resendme", "resendme@apexautobid.local", emailConfirmed: false);

        var result = await _sut.ResendConfirmationAsync(
            user.Id, "admin", actorIsAdmin: true, "https://localhost:5001", TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.Success, result.Status);

        await _emailSender.Received(1).SendConfirmationLinkAsync(
            Arg.Any<ApplicationUser>(), "resendme@apexautobid.local", Arg.Any<string>());

        Assert.Single(_fixture.DbContext.AuditEntries, a => a.Action == "AdminConfirmationResent" && a.EntityId == user.Id);
    }

    [Fact]
    public async Task ResendConfirmationAsync_AlreadyConfirmedUser_ReturnsValidationFailed()
    {
        var user = await SeedUserAsync("alreadyconfirmed", "alreadyconfirmed@apexautobid.local", emailConfirmed: true);

        var result = await _sut.ResendConfirmationAsync(
            user.Id, "admin", actorIsAdmin: true, "https://localhost:5001", TestContext.Current.CancellationToken);

        Assert.Equal(AdminActionStatus.ValidationFailed, result.Status);
        Assert.Empty(_fixture.DbContext.AuditEntries);
    }

    // ── SearchUsersAsync — search + paging ────────────────────────────────────────
    [Fact]
    public async Task SearchUsersAsync_MatchesUserNameCaseInsensitively()
    {
        await SeedUserAsync("alice", "alice@apexautobid.local");
        await SeedUserAsync("bob", "bob@apexautobid.local");

        var result = await _sut.SearchUsersAsync("ALI", page: 1, pageSize: 20, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("alice", Assert.Single(result.Items).UserName);
    }

    [Fact]
    public async Task SearchUsersAsync_NoSearchTerm_PagesAllUsers()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedUserAsync($"user{i}", $"user{i}@apexautobid.local");
        }

        var result = await _sut.SearchUsersAsync(null, page: 1, pageSize: 2, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    // ── GetStatsAsync — counts ────────────────────────────────────────────────────
    [Fact]
    public async Task GetStatsAsync_ReturnsTotalConfirmedAndLockedCounts()
    {
        var confirmedUser = await SeedUserAsync("confirmeduser", "confirmeduser@apexautobid.local", emailConfirmed: true);
        await SeedUserAsync("unconfirmeduser2", "unconfirmeduser2@apexautobid.local", emailConfirmed: false);
        var lockedUser = await SeedUserAsync("lockeduser", "lockeduser@apexautobid.local");
        _ = await _fixture.UserManager.SetLockoutEndDateAsync(lockedUser, DateTimeOffset.UtcNow.AddYears(1));

        var stats = await _sut.GetStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.Total);
        Assert.Equal(2, stats.Confirmed); // confirmedUser + lockedUser (SeedUserAsync defaults EmailConfirmed=true)
        Assert.Equal(1, stats.Locked);
        Assert.True(confirmedUser.EmailConfirmed);
    }
}
