using System.Text.Json;
using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
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

    public async Task<AuctionDetailDto?> GetAuctionByIdAsync(Guid id, string? requestingUser)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return null;

        // Two-hop mapping (Auction → AuctionDto → AuctionDetailDto) deliberately reuses rule 2
        // (Auction → AuctionDto) rather than duplicating the Item-flatten/Status/Images
        // projections a second time in AuctionMappingConfig — see rule 2b's remarks there.
        var baseDto = mapper.Map<AuctionDto>(auction);
        var dto = mapper.Map<AuctionDetailDto>(baseDto);

        // Post-sale contact exchange (Requirements §3.1 / Tasks.md Phase 5 Task 19.1).
        // Fail-safe redaction: both fields start (and, for every caller below the one narrow
        // exception, stay) null — regardless of what a future Mapster convention change might
        // otherwise copy onto AuctionDetailDto. Only the exact counterparty of a SOLD auction
        // (Status = Finished with a recorded Winner) ever sees the other party's email, and each
        // side sees only the ONE field relevant to them, read directly from the tracked entity
        // (never from anything Mapster produced) so this is the single source of truth for the
        // redaction decision.
        dto.SellerEmail = null;
        dto.WinnerEmail = null;

        var isSold = auction.Status == Status.Finished && auction.Winner is not null;
        if (isSold && requestingUser is not null)
        {
            if (requestingUser == auction.Seller)
                dto.WinnerEmail = auction.WinnerEmail;
            else if (requestingUser == auction.Winner)
                dto.SellerEmail = auction.SellerEmail;
        }

        return dto;
    }

    public async Task<AuctionCreateResult> CreateAuctionAsync(
        CreateAuctionDto dto,
        string seller,
        string sellerEmail,
        bool isAdmin)
    {
        // Gallery enforcement (Task 18.6) runs before any DB work — an invalid gallery
        // must not create a partially-formed auction.
        var galleryError = await ValidateGalleryAsync(dto.Images, CancellationToken.None);
        if (galleryError is not null)
            return new AuctionCreateResult(galleryError.Value, null);

        // mapper.Map (not the raw dto.Adapt<Auction>() extension method) is required here:
        // AddApplicationServices() registers AuctionMappingConfig on a fresh, DI-scoped
        // TypeAdapterConfig rather than the static TypeAdapterConfig.GlobalSettings, so the
        // ambient .Adapt<T>() extension method (which always resolves against GlobalSettings)
        // would silently miss the CreateAuctionDto → Auction custom rule (in particular the
        // nested Item mapping, which has no like-named source property to fall back to by
        // convention) and leave Auction.Item null.
        var auction = mapper.Map<Auction>(dto);
        auction.Seller = seller;
        auction.SellerEmail = sellerEmail;

        repository.Add(auction);

        // Map the read DTO once — reused both for the outbox publish and the return value.
        // EF Core assigns the Guid key at Add() time so Id is already populated here.
        var auctionDto = mapper.Map<AuctionDto>(auction);

        // Publish BEFORE SaveChangesAsync so the outbox message and the domain row
        // are written in the same database transaction (bus outbox requirement).
        await publishEndpoint.Publish(mapper.Map<AuctionCreated>(auctionDto));

        // Append-only audit record (Requirements §13.3) — added to the context BEFORE
        // SaveChangesAsync so it commits in the SAME transaction as the auction insert.
        // Never includes SellerEmail or any other secret/PII.
        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = seller,
            ActorIsAdmin = isAdmin,
            Action = "AuctionCreated",
            EntityType = "Auction",
            EntityId = auction.Id.ToString(),
            Data = JsonSerializer.Serialize(new
            {
                auction.Id,
                auction.Seller,
                auction.Item.Make,
                auction.Item.Model,
                auction.Item.Year,
                auction.ReservePrice,
                auction.AuctionEnd
            })
        });

        var saved = await repository.SaveChangesAsync();
        return saved
            ? new AuctionCreateResult(AuctionWriteResult.Success, auctionDto)
            : new AuctionCreateResult(AuctionWriteResult.SaveFailed, null);
    }

    public async Task<AuctionWriteResult> UpdateAuctionAsync(
        Guid id,
        UpdateAuctionDto dto,
        string requestingUser,
        bool isAdmin)
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
                .Select(i => mapper.Map<ItemImage>(i))
                .ToList();

            repository.ReplaceGallery(auction.Item, newImages);
        }

        auction.UpdatedAt = DateTime.UtcNow;

        // Map from the updated tracked entity, then publish BEFORE SaveChangesAsync
        // so the outbox message and the domain row commit atomically.
        var auctionDto = mapper.Map<AuctionDto>(auction);
        await publishEndpoint.Publish(mapper.Map<AuctionUpdated>(auctionDto));

        // Append-only audit record (Requirements §13.3) — summarizes only the requested
        // changes (non-null dto fields), never SellerEmail/WinnerEmail or any secret.
        // Added to the context BEFORE SaveChangesAsync so it commits atomically.
        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = requestingUser,
            ActorIsAdmin = isAdmin,
            Action = "AuctionUpdated",
            EntityType = "Auction",
            EntityId = id.ToString(),
            Data = JsonSerializer.Serialize(new
            {
                dto.Make,
                dto.Model,
                dto.Color,
                dto.Mileage,
                dto.Year,
                ImagesCount = dto.Images?.Count
            })
        });

        // SaveChangesAsync returns 0 when the submitted values are identical to
        // the stored ones (EF detects no dirty columns) — that is still a logical
        // success, the record is already in the requested state. Genuine failures
        // throw (e.g. DbUpdateException), surfaced by the global handler (Task 19).
        await repository.SaveChangesAsync();
        return AuctionWriteResult.Success;
    }

    public async Task<AuctionWriteResult> DeleteAuctionAsync(Guid id, string requestingUser, bool isAdmin)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AuctionWriteResult.NotFound;

        if (auction.Seller != requestingUser)
            return AuctionWriteResult.Forbidden;

        // Snapshot the fields needed for the audit Data payload before the auction is
        // staged for removal — Remove() doesn't clear in-memory properties, but this
        // keeps the audit payload construction unambiguous regardless of EF behavior.
        var auditData = JsonSerializer.Serialize(new
        {
            auction.Id,
            auction.Seller,
            auction.Item.Make,
            auction.Item.Model
        });

        repository.Remove(auction);

        // Publish BEFORE SaveChangesAsync for atomic outbox + domain commit.
        await publishEndpoint.Publish(new AuctionDeleted(auction.Id.ToString()));

        // Append-only audit record (Requirements §13.3) — added to the context BEFORE
        // SaveChangesAsync so it commits in the SAME transaction as the auction delete.
        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = requestingUser,
            ActorIsAdmin = isAdmin,
            Action = "AuctionDeleted",
            EntityType = "Auction",
            EntityId = auction.Id.ToString(),
            Data = auditData
        });

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

        // SortOrder integrity. The mapping preserves client-supplied SortOrder verbatim, the DB
        // enforces a unique (ItemId, SortOrder) index, and SortOrder 0 is the primary image. Reject
        // galleries that would violate those invariants here (clean 400) instead of letting them
        // surface as a DbUpdateException/500 at SaveChanges: no negatives, no duplicates, and a
        // primary at 0 must be present. This runs before any storage HEAD so a malformed gallery
        // is rejected without an object-store round-trip.
        var sortOrders = images.Select(img => img.SortOrder).ToList();
        if (sortOrders.Any(order => order < 0)
            || sortOrders.Distinct().Count() != sortOrders.Count
            || !sortOrders.Contains(0))
        {
            return AuctionWriteResult.InvalidImages;
        }

        var maxBytes = (long)imagesOptions.Value.MaxSizeMB * 1024 * 1024;
        var prefix = $"{imagesOptions.Value.PublicBaseUrl.TrimEnd('/')}/{imagesOptions.Value.Bucket}/";
        foreach (var img in images)
        {
            // External URLs are exempt from the size check but still counted (handled by the count check above).
            // Scheme/host are compared case-insensitively (URLs are case-insensitive there); the extracted
            // key is separately Guid.TryParse-validated and canonicalized below, so a case-insensitive
            // prefix match can't let anything unsafe through.
            if (!img.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

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
