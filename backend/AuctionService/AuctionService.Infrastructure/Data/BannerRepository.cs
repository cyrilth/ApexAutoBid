using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Infrastructure.Data;

/// <summary>EF Core implementation of <see cref="IBannerRepository"/>.</summary>
public class BannerRepository(AuctionDbContext dbContext) : IBannerRepository
{
    public async Task<List<Banner>> GetAllAsync()
    {
        return await dbContext.Banners
            .OrderByDescending(b => b.ActiveFrom)
            .ToListAsync();
    }

    public async Task<List<Banner>> GetActiveAsync(BannerScope? scope, Guid? auctionId, DateTime now)
    {
        var query = dbContext.Banners
            .Where(b => b.ActiveFrom <= now && now <= b.ActiveUntil)
            .AsQueryable();

        if (scope.HasValue)
            query = query.Where(b => b.Scope == scope.Value);

        if (auctionId.HasValue)
            query = query.Where(b => b.AuctionId == auctionId.Value);

        return await query.OrderByDescending(b => b.ActiveFrom).ToListAsync();
    }

    public async Task<Banner?> GetByIdAsync(Guid id)
    {
        return await dbContext.Banners.FirstOrDefaultAsync(b => b.Id == id);
    }

    public void Add(Banner banner)
    {
        dbContext.Banners.Add(banner);
    }

    public void Remove(Banner banner)
    {
        dbContext.Banners.Remove(banner);
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
