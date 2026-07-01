using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Contracts;
using Mapster;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Options;

namespace AuctionService.Application.Services;

/// <summary>
/// Application-service implementation. Named <c>AuctionAppService</c> (not
/// <c>AuctionService</c>) to avoid a CS0118 clash with the root namespace
/// <c>AuctionService</c>.
/// </summary>
public class AuctionAppService(
    IAuctionRepository repository,
    IMapper mapper,
    IPublishEndpoint publishEndpoint,
    IImageStorage storage,
    IOptions<ImagesOptions> imagesOptions) : IAuctionService
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

    public async Task<AuctionCreateResult> CreateAuctionAsync(
        CreateAuctionDto dto,
        string seller,
        string sellerEmail)
    {
        // Gallery enforcement (Task 18.6) runs before any DB work — an invalid gallery
        // must not create a partially-formed auction.
        var galleryError = await ValidateGalleryAsync(dto.Images, CancellationToken.None);
        if (galleryError is not null)
            return new AuctionCreateResult(galleryError.Value, null);

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
        return saved
            ? new AuctionCreateResult(AuctionWriteResult.Success, auctionDto)
            : new AuctionCreateResult(AuctionWriteResult.SaveFailed, null);
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

        // Gallery enforcement (Task 18.6): validated before any change is applied to the
        // tracked entity, so an invalid submitted gallery leaves the auction untouched.
        if (dto.Images is not null)
        {
            var galleryError = await ValidateGalleryAsync(dto.Images, CancellationToken.None);
            if (galleryError is not null)
                return galleryError.Value;
        }

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

    /// <summary>
    /// Server-side gallery enforcement (Task 18.6): validates the submitted image count
    /// against <c>Images:MaxPerAuction</c>, then HEAD-verifies the actual size of every
    /// platform-hosted image (a <see cref="ImageDto.Url"/> under the configured
    /// <c>Images:PublicBaseUrl</c>/<c>Images:Bucket</c> prefix) against <c>Images:MaxSizeMB</c>,
    /// rejecting anything missing or oversized. Externally hosted image URLs are exempt
    /// from the size check (the platform doesn't control them) but still count toward the
    /// per-auction limit.
    /// </summary>
    /// <returns>
    /// <see cref="AuctionWriteResult.InvalidImages"/> when the gallery is invalid, otherwise
    /// <see langword="null"/>.
    /// </returns>
    private async Task<AuctionWriteResult?> ValidateGalleryAsync(
        IReadOnlyList<ImageDto> images,
        CancellationToken ct)
    {
        var max = imagesOptions.Value.MaxPerAuction;
        if (images.Count < 1 || images.Count > max)
            return AuctionWriteResult.InvalidImages;

        var maxBytes = (long)imagesOptions.Value.MaxSizeMB * 1024 * 1024;
        var prefix = $"{imagesOptions.Value.PublicBaseUrl.TrimEnd('/')}/{imagesOptions.Value.Bucket}/";
        foreach (var img in images)
        {
            // External URLs are exempt from the size check but still counted (handled by the count check above).
            if (!img.Url.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var rawKey = img.Url[prefix.Length..];
            // Only bare GUID keys are objects we issued via upload-url. Anything else (path traversal
            // like "../..", sub-paths, crafted URLs) is rejected outright — never fed to a HEAD/DELETE
            // against the storage host. Use the canonical GUID form so validation and use can't diverge.
            if (!Guid.TryParse(rawKey, out var parsed)) return AuctionWriteResult.InvalidImages;
            var key = parsed.ToString();

            var size = await storage.TryGetObjectSizeAsync(key, ct);
            // A platform-hosted image must actually exist and be within the size limit. Note: a
            // legitimately-uploaded object can never exceed the limit — CreatePresignedUpload signs
            // Content-Length into the PUT URL — so this is purely a defense-in-depth/missing-object
            // check. Deletion is intentionally NOT performed here: GET auctions is anonymous, so any
            // client can harvest a platform object key and reference it in a gallery it doesn't own;
            // deleting on validation failure would let that client destroy objects it has no right to.
            if (size is null || size > maxBytes) return AuctionWriteResult.InvalidImages;
        }
        return null;
    }
}
