using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Mapster;
using MapsterMapper;

namespace AuctionService.Application.Services;

/// <summary>
/// Application-service implementation. Named <c>AuctionAppService</c> (not
/// <c>AuctionService</c>) to avoid a CS0118 clash with the root namespace
/// <c>AuctionService</c>.
/// </summary>
public class AuctionAppService(IAuctionRepository repository, IMapper mapper) : IAuctionService
{
    public async Task<List<AuctionDto>> GetAuctionsAsync(DateTime? updatedAfter)
    {
        var auctions = await repository.GetAllAsync(updatedAfter);
        return mapper.Map<List<AuctionDto>>(auctions);
    }

    public async Task<AuctionDto?> GetAuctionByIdAsync(Guid id)
    {
        var auction = await repository.GetByIdAsync(id);
        return auction is null ? null : mapper.Map<AuctionDto>(auction);
    }

    public async Task<AuctionDto?> CreateAuctionAsync(
        CreateAuctionDto dto,
        string seller,
        string sellerEmail)
    {
        var auction = dto.Adapt<Auction>();
        auction.Seller = seller;
        auction.SellerEmail = sellerEmail;

        repository.Add(auction);

        var saved = await repository.SaveChangesAsync();
        return saved ? mapper.Map<AuctionDto>(auction) : null;
    }

    public async Task<AuctionWriteResult> UpdateAuctionAsync(
        Guid id,
        UpdateAuctionDto dto,
        string requestingUser)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AuctionWriteResult.NotFound;

        if (auction.Seller != requestingUser)
            return AuctionWriteResult.Forbidden;

        // Apply Auction-level partial update (IgnoreNullValues configured in mapping).
        mapper.Map(dto, auction);

        // Apply Item-level partial update (Images intentionally ignored by mapping config).
        mapper.Map(dto, auction.Item);

        // Gallery swap: replace entire image list when dto.Images is non-null.
        if (dto.Images is not null)
        {
            var newImages = dto.Images
                .Select(i => i.Adapt<ItemImage>())
                .ToList();

            repository.ReplaceGallery(auction.Item, newImages);
        }

        auction.UpdatedAt = DateTime.UtcNow;

        return await repository.SaveChangesAsync()
            ? AuctionWriteResult.Success
            : AuctionWriteResult.SaveFailed;
    }

    public async Task<AuctionWriteResult> DeleteAuctionAsync(Guid id, string requestingUser)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AuctionWriteResult.NotFound;

        if (auction.Seller != requestingUser)
            return AuctionWriteResult.Forbidden;

        repository.Remove(auction);

        return await repository.SaveChangesAsync()
            ? AuctionWriteResult.Success
            : AuctionWriteResult.SaveFailed;
    }
}
