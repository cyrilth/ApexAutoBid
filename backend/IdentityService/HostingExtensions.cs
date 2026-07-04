using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService;

internal static class HostingExtensions
{
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        _ = builder.Services.AddRazorPages();

        _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        _ = builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _ = builder.Services
            .AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // Use a large chunk size for diagnostic data in development so long entries
                // aren't split across multiple console log lines.
                if (builder.Environment.IsDevelopment())
                {
                    options.Diagnostics.ChunkSize = 1024 * 1024 * 10; // 10 MB
                }
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<ApplicationUser>()
            // Replaces AddAspNetIdentity's default IProfileService registration with
            // Services/ProfileService.cs, which adds the username/email/email_verified/role
            // access-token claims (Phase 3 Task 3 / Requirements.md §3.4).
            .AddProfileService<ProfileService>()
            .AddLicenseSummary();

        // No external authentication provider is registered here. The template's demo
        // "Sign-in with demo.duendesoftware.com" OIDC provider has been removed (Phase 3 Task 1
        // review follow-up) — it rendered a live login button that auto-provisioned local
        // accounts for anyone with a demo.duendesoftware.com login, which has no place in this
        // app even in dev. Google external login is a later, separate task (Phase 3 Task 15);
        // Pages/ExternalLogin/* are left in place for it — the login page only lists external
        // providers it discovers via IAuthenticationSchemeProvider (Pages/Account/Login/Index.cshtml.cs),
        // so with no scheme registered here, those pages are simply unreachable, not broken.

        // add `.PersistKeysTo…()` and `.ProtectKeysWith…()` calls
        // see more at https://docs.duendesoftware.com/general/data-protection
        _ = builder.Services.AddDataProtection()
                   .SetApplicationName("IdentityServer");

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            _ = app.UseDeveloperExceptionPage();
        }

        _ = app.UseStaticFiles();
        _ = app.UseRouting();
        _ = app.UseIdentityServer();
        _ = app.UseAuthorization();

        _ = app.MapRazorPages()
            .RequireAuthorization();

        return app;
    }
}
