using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Contracts;
using Mapster;
using MapsterMapper;
using MassTransit;

namespace AuctionService.Application.Services;

/// <summary>
/// Application-service implementation. Named <c>AuctionAppService</c> (not
/// <c>AuctionService</c>) to avoid a CS0118 clash with the root namespace
/// <c>AuctionService</c>.
/// </summary>
public class AuctionAppService(
    IAuctionRepository repository,
    IMapper mapper,
    IPublishEndpoint publishEndpoint) : IAuctionService
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

        // Map the read DTO once — reused both for the outbox publish and the return value.
        // EF Core assigns the Guid key at Add() time so Id is already populated here.
        var auctionDto = mapper.Map<AuctionDto>(auction);

        // Publish BEFORE SaveChangesAsync so the outbox message and the domain row
        // are written in the same database transaction (bus outbox requirement).
        await publishEndpoint.Publish(mapper.Map<AuctionCreated>(auctionDto));

        var saved = await repository.SaveChangesAsync();
        return saved ? auctionDto : null;
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

        // Map from the updated tracked entity, then publish BEFORE SaveChangesAsync
        // so the outbox message and the domain row commit atomically.
        var auctionDto = mapper.Map<AuctionDto>(auction);
        await publishEndpoint.Publish(mapper.Map<AuctionUpdated>(auctionDto));

        // SaveChangesAsync returns 0 when the submitted values are identical to
        // the stored ones (EF detects no dirty columns) — that is still a logical
        // success, the record is already in the requested state. Genuine failures
        // throw (e.g. DbUpdateException), surfaced by the global handler (Task 19).
        await repository.SaveChangesAsync();
        return AuctionWriteResult.Success;
    }

    public async Task<AuctionWriteResult> DeleteAuctionAsync(Guid id, string requestingUser)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AuctionWriteResult.NotFound;

        if (auction.Seller != requestingUser)
            return AuctionWriteResult.Forbidden;

        repository.Remove(auction);

        // Publish BEFORE SaveChangesAsync for atomic outbox + domain commit.
        await publishEndpoint.Publish(new AuctionDeleted(auction.Id.ToString()));

        return await repository.SaveChangesAsync()
            ? AuctionWriteResult.Success
            : AuctionWriteResult.SaveFailed;
    }
}
