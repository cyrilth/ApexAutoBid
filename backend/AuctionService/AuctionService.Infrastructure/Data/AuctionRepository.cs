using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Infrastructure.Data;

/// <summary>
/// EF Core implementation of <see cref="IAuctionRepository"/>.
/// </summary>
public class AuctionRepository(AuctionDbContext dbContext) : IAuctionRepository
{
    public async Task<List<Auction>> GetAllAsync(DateTime? updatedAfter)
    {
        var query = dbContext.Auctions
            .Include(a => a.Item)
            .ThenInclude(i => i.Images)
            .OrderBy(a => a.Item.Make)
            .ThenBy(a => a.Item.Model)
            .AsQueryable();

        if (updatedAfter.HasValue)
        {
            query = query.Where(a => a.UpdatedAt > updatedAfter.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<Auction?> GetByIdAsync(Guid id)
    {
        return await dbContext.Auctions
            .Include(a => a.Item)
            .ThenInclude(i => i.Images)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public void Add(Auction auction)
    {
        dbContext.Auctions.Add(auction);
    }

    public void Remove(Auction auction)
    {
        dbContext.Auctions.Remove(auction);
    }

    public void ReplaceGallery(Item item, List<ItemImage> newImages)
    {
        dbContext.RemoveRange(item.Images);

        foreach (var img in newImages)
        {
            img.ItemId = item.Id;
        }

        item.Images = newImages;
    }

    public async Task<bool> SaveChangesAsync()
    {
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> TryRaiseHighBidAsync(Guid auctionId, int amount)
    {
        var rows = await dbContext.Auctions
            .Where(a => a.Id == auctionId
                && a.Status == Status.Live
                && (a.CurrentHighBid == null || a.CurrentHighBid < amount))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CurrentHighBid, amount)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));

        return rows > 0;
    }
}
