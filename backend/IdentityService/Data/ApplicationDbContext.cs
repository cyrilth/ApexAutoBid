using IdentityService.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Phase 11 Task 2.9 — append-only admin-action audit trail (Requirements.md §13.3). Lives in
    // this same DbContext (not a separate one) so AdminUserService can write an AuditEntry row in
    // the SAME database transaction as the Identity mutation it describes.
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);

        // Mirrors AuctionService.Infrastructure.Data.AuctionDbContext's identical AuditEntry
        // configuration — plain required-string columns, Data stored as free-form JSON text
        // with no fixed schema (never queried directly, only inspected in the datastore).
        builder.Entity<AuditEntry>(audit =>
        {
            audit.HasKey(a => a.Id);

            audit.Property(a => a.Actor).IsRequired();
            audit.Property(a => a.Action).IsRequired();
            audit.Property(a => a.EntityType).IsRequired();
            audit.Property(a => a.EntityId).IsRequired();
            audit.Property(a => a.Data).IsRequired();
        });
    }
}
