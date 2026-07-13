using System.Text;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Dtos.Admin;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

/// <summary>
/// Implements the admin user-management operations (Phase 11 Task 2 / Requirements.md §10.1)
/// backing <see cref="Controllers.AdminUsersController"/>.
/// <para>
/// <b>Auditing (Requirements.md §13.3):</b> every mutating method writes an
/// <see cref="AuditEntry"/> via <see cref="ApplicationDbContext"/>. ASP.NET Core Identity's
/// EF Core store has <c>AutoSaveChanges = true</c> by default, so a <see cref="UserManager{TUser}"/>
/// call (e.g. <c>CreateAsync</c>, <c>AddToRolesAsync</c>) commits its own
/// <c>SaveChangesAsync</c> internally before this method ever adds the <see cref="AuditEntry"/>
/// row. To still make the Identity mutation and its audit row commit as a single atomic unit —
/// matching AuctionService/BiddingService's "same <c>SaveChangesAsync</c> call" pattern as
/// closely as Identity's internals allow — every such method wraps BOTH steps in an explicit
/// database transaction (<see cref="DbContext.Database"/>.BeginTransactionAsync) and rolls back
/// on any Identity-mutation failure, so no audit row is ever written for an operation that did
/// not actually take effect. <c>Data</c> never includes email addresses, tokens, or password
/// material (Requirements.md §13.3/§13.5) — only usernames and non-sensitive summary fields.
/// </para>
/// <para>
/// <b>Logging:</b> message templates only, never string interpolation; never logs an email
/// address or password/token material (Requirements.md §13.5) — only user ids/usernames.
/// </para>
/// </summary>
public class AdminUserService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext dbContext,
    IEmailSender<ApplicationUser> emailSender,
    ILogger<AdminUserService> logger) : IAdminUserService
{
    private const int MaxPageSize = 100;

    public async Task<UserListResultDto> SearchUsersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        // UserManager.Users is an IQueryable<ApplicationUser> when the underlying store supports
        // IQueryableUserStore — true for the real EF Core store (AddEntityFrameworkStores) used
        // in every environment this runs in.
        var query = userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matched against the ALREADY-normalized (uppercase-invariant) username/email columns
            // rather than EF.Functions.ILike (Npgsql-only) — EF.Functions.Like is a portable
            // ANSI SQL LIKE translatable by both this service's real Npgsql provider and the
            // SQLite in-memory provider AdminUserServiceTests uses, and comparing two
            // already-uppercased operands makes the match case-insensitive without relying on
            // either provider's default column collation.
            var normalizedTerm = search.Trim().ToUpperInvariant();
            query = query.Where(u =>
                EF.Functions.Like(u.NormalizedUserName!, $"%{normalizedTerm}%") ||
                EF.Functions.Like(u.NormalizedEmail!, $"%{normalizedTerm}%"));
        }

        var totalCount = await query.CountAsync(ct);

        var pageOfUsers = await query
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UserListItemDto>(pageOfUsers.Count);
        foreach (var user in pageOfUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new UserListItemDto
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                LockedOut = await userManager.IsLockedOutAsync(user),
                LockoutEnd = user.LockoutEnd,
                Roles = [.. roles],
            });
        }

        return new UserListResultDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<UserStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var total = await userManager.Users.CountAsync(ct);
        var confirmed = await userManager.Users.CountAsync(u => u.EmailConfirmed, ct);

        // Consistent with UserManager.IsLockedOutAsync's own definition (LockoutEnd set AND in
        // the future) — not read per-user via IsLockedOutAsync here since that would be an N+1
        // round trip. The "still in the future" comparison is done client-side (not
        // `u.LockoutEnd > now` in the query) deliberately: EF Core's SQLite provider (used by
        // AdminUserServiceTests) cannot translate a DateTimeOffset ">" comparison against a
        // parameter, only Npgsql (production) can — so the DB-side filter is narrowed to the
        // portable, index-friendly "has a LockoutEnd at all" null check, and only that (normally
        // small) set of rows is compared against "now" in memory. Behaviorally identical to a
        // single server-side query on Npgsql; portable across both providers.
        var lockoutEnds = await userManager.Users
            .Where(u => u.LockoutEnd != null)
            .Select(u => u.LockoutEnd)
            .ToListAsync(ct);
        var locked = lockoutEnds.Count(end => end > now);

        return new UserStatsDto { Total = total, Confirmed = confirmed, Locked = locked };
    }

    public async Task<AdminActionResult<CreateUserResponseDto>> CreateUserAsync(
        CreateUserRequestDto dto, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = dto.UserName,
            Email = dto.Email,
            EmailConfirmed = dto.PreConfirmed,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var createResult = await userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return AdminActionResult<CreateUserResponseDto>.Invalid(createResult.Errors.Select(e => e.Description));
        }

        // Data carries the username only — never the email address (Requirements.md §13.5).
        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActorIsAdmin = actorIsAdmin,
            Action = "AdminUserCreated",
            EntityType = "User",
            EntityId = user.Id,
            Data = JsonSerializer.Serialize(new { user.UserName, dto.PreConfirmed }),
        });
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Admin {Actor} created user {UserId} (PreConfirmed={PreConfirmed})",
            actor, user.Id, dto.PreConfirmed);

        // Best-effort, outside the transaction — mirrors Register/Index.cshtml.cs: a down mail
        // relay must never fail the (already-committed) account creation.
        if (!dto.PreConfirmed)
        {
            await SendConfirmationEmailAsync(user, linkBaseUrl, ct);
        }

        return AdminActionResult<CreateUserResponseDto>.Ok(new CreateUserResponseDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email,
            EmailConfirmed = user.EmailConfirmed,
        });
    }

    public async Task<AdminActionResult<ResetPasswordResponseDto>> ResetPasswordAsync(
        string userId, ResetPasswordRequestDto dto, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AdminActionResult<ResetPasswordResponseDto>.NotFound();
        }

        if (dto.SendResetLink)
        {
            // GeneratePasswordResetTokenAsync (DataProtector-backed, like the email-confirmation
            // token) does not itself persist anything via the store, so a single SaveChangesAsync
            // for the audit row is already atomic with respect to this branch — no transaction
            // needed here (there is no Identity-store mutation to roll back).
            dbContext.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Actor = actor,
                ActorIsAdmin = actorIsAdmin,
                Action = "AdminPasswordResetLinkSent",
                EntityType = "User",
                EntityId = user.Id,
                Data = JsonSerializer.Serialize(new { user.UserName }),
            });
            await dbContext.SaveChangesAsync(ct);

            var resetLinkToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var encodedResetToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetLinkToken));
            var resetLink =
                $"{linkBaseUrl}/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedResetToken)}";

            try
            {
                await emailSender.SendPasswordResetLinkAsync(user, user.Email!, resetLink);
                logger.LogInformation("Password reset email queued for user {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                // Never pass the exception object to the logger — see SmtpEmailSender's own
                // remarks (its Message can embed the rejected recipient address).
                logger.LogWarning(
                    "Failed to queue password reset email for user {UserId} ({ExceptionType})",
                    user.Id, ex.GetType().Name);
            }

            logger.LogInformation("Admin {Actor} sent a password reset link to user {UserId}", actor, user.Id);
            return AdminActionResult<ResetPasswordResponseDto>.Ok(new ResetPasswordResponseDto { LinkSent = true });
        }

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return AdminActionResult<ResetPasswordResponseDto>.Invalid(
                ["NewPassword is required when SendResetLink is false."]);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, resetToken, dto.NewPassword);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return AdminActionResult<ResetPasswordResponseDto>.Invalid(result.Errors.Select(e => e.Description));
        }

        // Data never contains the new password (Requirements.md §13.3) — the response DTO below
        // is the one place it is returned, once, to the calling admin.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActorIsAdmin = actorIsAdmin,
            Action = "AdminPasswordReset",
            EntityType = "User",
            EntityId = user.Id,
            Data = JsonSerializer.Serialize(new { user.UserName }),
        });
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Admin {Actor} set a temporary password for user {UserId}", actor, user.Id);

        return AdminActionResult<ResetPasswordResponseDto>.Ok(new ResetPasswordResponseDto
        {
            LinkSent = false,
            TemporaryPassword = dto.NewPassword,
        });
    }

    public async Task<AdminActionResult<object?>> ResendConfirmationAsync(
        string userId, string actor, bool actorIsAdmin, string linkBaseUrl, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AdminActionResult<object?>.NotFound();
        }

        if (user.EmailConfirmed)
        {
            return AdminActionResult<object?>.Invalid(["The user's email is already confirmed."]);
        }

        // No Identity-store mutation here (token generation is stateless, like the reset-link
        // branch above) — a single SaveChangesAsync for the audit row is already atomic.
        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActorIsAdmin = actorIsAdmin,
            Action = "AdminConfirmationResent",
            EntityType = "User",
            EntityId = user.Id,
            Data = JsonSerializer.Serialize(new { user.UserName }),
        });
        await dbContext.SaveChangesAsync(ct);

        await SendConfirmationEmailAsync(user, linkBaseUrl, ct);

        logger.LogInformation("Admin {Actor} resent the confirmation email for user {UserId}", actor, user.Id);

        return AdminActionResult<object?>.Ok(null);
    }

    public async Task<AdminActionResult<RolesUpdateResponseDto>> UpdateRolesAsync(
        string userId, RolesUpdateRequestDto dto, string actor, bool actorIsAdmin, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AdminActionResult<RolesUpdateResponseDto>.NotFound();
        }

        var desiredRoles = dto.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var role in desiredRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                return AdminActionResult<RolesUpdateResponseDto>.Invalid([$"Role '{role}' does not exist."]);
            }
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var rolesToAdd = desiredRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        var rolesToRemove = currentRoles.Except(desiredRoles, StringComparer.OrdinalIgnoreCase).ToList();

        if (rolesToAdd.Count == 0 && rolesToRemove.Count == 0)
        {
            // No-op — already in the desired state. Not audited (nothing changed).
            return AdminActionResult<RolesUpdateResponseDto>.Ok(new RolesUpdateResponseDto
            {
                Id = user.Id,
                Roles = [.. currentRoles],
            });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        if (rolesToAdd.Count > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AdminActionResult<RolesUpdateResponseDto>.Invalid(addResult.Errors.Select(e => e.Description));
            }
        }

        if (rolesToRemove.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AdminActionResult<RolesUpdateResponseDto>.Invalid(removeResult.Errors.Select(e => e.Description));
            }
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActorIsAdmin = actorIsAdmin,
            Action = "AdminUserRolesChanged",
            EntityType = "User",
            EntityId = user.Id,
            Data = JsonSerializer.Serialize(new { user.UserName, Added = rolesToAdd, Removed = rolesToRemove }),
        });
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Admin {Actor} changed roles for user {UserId} (+{AddedCount}, -{RemovedCount})",
            actor, user.Id, rolesToAdd.Count, rolesToRemove.Count);

        var updatedRoles = await userManager.GetRolesAsync(user);
        return AdminActionResult<RolesUpdateResponseDto>.Ok(new RolesUpdateResponseDto
        {
            Id = user.Id,
            Roles = [.. updatedRoles],
        });
    }

    public async Task<AdminActionResult<LockResponseDto>> SetLockAsync(
        string userId, LockRequestDto dto, string actor, bool actorIsAdmin, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AdminActionResult<LockResponseDto>.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        if (dto.Locked)
        {
            // Every user gets LockoutEnabled=true at creation time (HostingExtensions'
            // options.Lockout.AllowedForNewUsers = true, applied by UserManager.CreateAsync to
            // both self-registered and admin-created accounts) — so setting LockoutEnd alone is
            // enough for UserManager.IsLockedOutAsync/SignInManager to actually enforce it.
            var lockoutEnd = dto.LockoutEnd ?? DateTimeOffset.UtcNow.AddYears(100);
            var setResult = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
            if (!setResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AdminActionResult<LockResponseDto>.Invalid(setResult.Errors.Select(e => e.Description));
            }
        }
        else
        {
            var setResult = await userManager.SetLockoutEndDateAsync(user, null);
            if (!setResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AdminActionResult<LockResponseDto>.Invalid(setResult.Errors.Select(e => e.Description));
            }

            // Also clears the failed-attempt counter so a just-unlocked account isn't one bad
            // login away from being immediately re-locked by ASP.NET Identity's own lockout logic.
            await userManager.ResetAccessFailedCountAsync(user);
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActorIsAdmin = actorIsAdmin,
            Action = dto.Locked ? "AdminUserLocked" : "AdminUserUnlocked",
            EntityType = "User",
            EntityId = user.Id,
            Data = JsonSerializer.Serialize(new { user.UserName, LockoutEnd = user.LockoutEnd }),
        });
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Admin {Actor} {LockAction} user {UserId}",
            actor, dto.Locked ? "locked" : "unlocked", user.Id);

        var lockedOut = await userManager.IsLockedOutAsync(user);
        return AdminActionResult<LockResponseDto>.Ok(new LockResponseDto
        {
            Id = user.Id,
            LockedOut = lockedOut,
            LockoutEnd = user.LockoutEnd,
        });
    }

    /// <summary>
    /// Shared confirmation-email link builder/sender, reused by <see cref="CreateUserAsync"/>
    /// (when not pre-confirmed) and <see cref="ResendConfirmationAsync"/>. Mirrors
    /// Pages/Account/Register/Index.cshtml.cs's exact token encoding and link shape so
    /// Pages/Account/ConfirmEmail.cshtml.cs (unchanged) can land either link identically.
    /// </summary>
    private async Task SendConfirmationEmailAsync(ApplicationUser user, string linkBaseUrl, CancellationToken ct)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationLink =
            $"{linkBaseUrl}/Account/ConfirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedToken)}";

        try
        {
            await emailSender.SendConfirmationLinkAsync(user, user.Email!, confirmationLink);
            logger.LogInformation("Confirmation email queued for user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            // Exception object deliberately not passed to the logger — see SmtpEmailSender's own
            // remarks (its Message can embed the rejected recipient address, Requirements.md §13.5).
            logger.LogWarning(
                "Failed to queue confirmation email for user {UserId} ({ExceptionType})",
                user.Id, ex.GetType().Name);
        }
    }
}
