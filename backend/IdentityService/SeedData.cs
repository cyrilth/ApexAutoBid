using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService;

/// <summary>
/// Seeds the default dev/demo users (Requirements.md §8.1: bob, alice, tom, admin — a shared
/// dev password, confirmed emails, admin additionally gets the "admin" role). Idempotent —
/// each user is looked up by username first and only created if absent, and the admin role
/// assignment is only applied if missing — so this is safe to call on every startup, the same
/// pattern AuctionService.Infrastructure.Data.DbInitializer uses for its own seed data.
/// </summary>
public class SeedData
{
    // Requirements.md §6: dev-only credentials are committed by design — this is the exact
    // shared password from §8.1. Never pass this to ILogger; every log call below logs
    // usernames only.
    private const string SharedDevPassword = "Pass123$";

    // Requirements.md §10: the "admin" role gates the admin dashboard / api/admin/* endpoints.
    private const string AdminRole = "admin";

    private static readonly (string Username, string Email, bool IsAdmin)[] SeedUsers =
    [
        ("bob", "bob@apexautobid.local", false),
        ("alice", "alice@apexautobid.local", false),
        ("tom", "tom@apexautobid.local", false),
        ("admin", "admin@apexautobid.local", true),
    ];

    public static async Task EnsureSeedDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SeedData>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(AdminRole));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create the '{AdminRole}' role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
            }

            logger.LogInformation("Created role {Role}", AdminRole);
        }

        foreach (var (username, email, isAdmin) in SeedUsers)
        {
            var user = await userManager.FindByNameAsync(username);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = username,
                    Email = email,
                    EmailConfirmed = true,
                };

                var createResult = await userManager.CreateAsync(user, SharedDevPassword);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create seed user {username}: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                }

                logger.LogInformation("Seeded user {Username}", username);
            }
            else
            {
                logger.LogDebug("Seed user {Username} already exists — skipping", username);
            }

            if (isAdmin && !await userManager.IsInRoleAsync(user, AdminRole))
            {
                var roleAssignResult = await userManager.AddToRoleAsync(user, AdminRole);
                if (!roleAssignResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to assign the '{AdminRole}' role to {username}: {string.Join(", ", roleAssignResult.Errors.Select(e => e.Description))}");
                }

                logger.LogInformation("Assigned role {Role} to {Username}", AdminRole, username);
            }
        }
    }
}
