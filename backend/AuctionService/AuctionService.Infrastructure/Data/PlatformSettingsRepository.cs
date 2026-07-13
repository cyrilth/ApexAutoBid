using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Infrastructure.Data;

/// <summary>EF Core implementation of <see cref="IPlatformSettingsRepository"/>.</summary>
public class PlatformSettingsRepository(AuctionDbContext dbContext) : IPlatformSettingsRepository
{
    public async Task<PlatformSettings?> GetAsync()
    {
        // At most one row ever exists — see the entity's own remarks.
        return await dbContext.PlatformSettings.FirstOrDefaultAsync();
    }

    public void Add(PlatformSettings settings)
    {
        dbContext.PlatformSettings.Add(settings);
    }

    public void AddAudit(AuditEntry entry)
    {
        dbContext.AuditEntries.Add(entry);
    }

    public async Task<bool> SaveChangesAsync()
    {
        return await dbContext.SaveChangesAsync() > 0;
    }
}
