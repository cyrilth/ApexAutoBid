using System.Text.Json;
using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;

namespace AuctionService.Application.Services;

/// <summary>Application-service implementation of <see cref="IBannerService"/>.</summary>
public class BannerAppService(
    IBannerRepository repository,
    IPublishEndpoint publishEndpoint) : IBannerService
{
    public async Task<List<BannerDto>> GetAllAsync()
    {
        var banners = await repository.GetAllAsync();
        return banners.Select(ToDto).ToList();
    }

    public async Task<List<BannerDto>> GetActiveAsync(string? scope, Guid? auctionId)
    {
        BannerScope? parsedScope = null;

        if (!string.IsNullOrWhiteSpace(scope))
        {
            // Ordinal, case-sensitive match against the enum names — mirrors the exact
            // "Global"/"HomePage"/"Auction" literals the BannerPublished event contract carries.
            // An unrecognized value yields an empty result set rather than an error (see this
            // method's own remarks on IBannerService).
            if (!Enum.TryParse<BannerScope>(scope, ignoreCase: false, out var parsed))
                return [];
            parsedScope = parsed;
        }

        var banners = await repository.GetActiveAsync(parsedScope, auctionId, DateTime.UtcNow);
        return banners.Select(ToDto).ToList();
    }

    public async Task<BannerCreateResult> CreateAsync(CreateBannerDto dto, string createdBy)
    {
        var validationError = Validate(dto.Scope, dto.AuctionId, dto.ActiveFrom, dto.ActiveUntil, out var parsedScope);
        if (validationError is not null)
            return new BannerCreateResult(validationError.Value, null);

        var banner = new Banner
        {
            Id = Guid.NewGuid(),
            Message = dto.Message,
            Scope = parsedScope,
            AuctionId = dto.AuctionId,
            ActiveFrom = dto.ActiveFrom,
            ActiveUntil = dto.ActiveUntil,
            CreatedBy = createdBy
        };

        repository.Add(banner);

        // Publish BEFORE SaveChangesAsync so the outbox message and the domain row are
        // written in the same database transaction (bus outbox requirement).
        await publishEndpoint.Publish(ToBannerPublished(banner));

        // Append-only audit record (Requirements §13.3) — added to the context BEFORE
        // SaveChangesAsync so it commits in the SAME transaction as the banner insert.
        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = createdBy,
            ActorIsAdmin = true,
            Action = "BannerCreated",
            EntityType = "Banner",
            EntityId = banner.Id.ToString(),
            Data = JsonSerializer.Serialize(new
            {
                banner.Id,
                banner.Message,
                Scope = banner.Scope.ToString(),
                banner.AuctionId,
                banner.ActiveFrom,
                banner.ActiveUntil
            })
        });

        await repository.SaveChangesAsync();
        return new BannerCreateResult(BannerWriteResult.Success, ToDto(banner));
    }

    public async Task<BannerWriteResult> UpdateAsync(Guid id, UpdateBannerDto dto, string updatedBy)
    {
        var banner = await repository.GetByIdAsync(id);
        if (banner is null)
            return BannerWriteResult.NotFound;

        var validationError = Validate(dto.Scope, dto.AuctionId, dto.ActiveFrom, dto.ActiveUntil, out var parsedScope);
        if (validationError is not null)
            return validationError.Value;

        banner.Message = dto.Message;
        banner.Scope = parsedScope;
        banner.AuctionId = dto.AuctionId;
        banner.ActiveFrom = dto.ActiveFrom;
        banner.ActiveUntil = dto.ActiveUntil;

        // Publish BEFORE SaveChangesAsync for atomic outbox + domain commit.
        await publishEndpoint.Publish(ToBannerPublished(banner));

        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = updatedBy,
            ActorIsAdmin = true,
            Action = "BannerUpdated",
            EntityType = "Banner",
            EntityId = banner.Id.ToString(),
            Data = JsonSerializer.Serialize(new
            {
                banner.Id,
                banner.Message,
                Scope = banner.Scope.ToString(),
                banner.AuctionId,
                banner.ActiveFrom,
                banner.ActiveUntil
            })
        });

        await repository.SaveChangesAsync();
        return BannerWriteResult.Success;
    }

    public async Task<BannerWriteResult> DeleteAsync(Guid id, string deletedBy)
    {
        var banner = await repository.GetByIdAsync(id);
        if (banner is null)
            return BannerWriteResult.NotFound;

        var auditData = JsonSerializer.Serialize(new { banner.Id, banner.Message });

        repository.Remove(banner);

        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = deletedBy,
            ActorIsAdmin = true,
            Action = "BannerDeleted",
            EntityType = "Banner",
            EntityId = banner.Id.ToString(),
            Data = auditData
        });

        await repository.SaveChangesAsync();
        return BannerWriteResult.Success;
    }

    private static BannerWriteResult? Validate(
        string scope, Guid? auctionId, DateTime activeFrom, DateTime activeUntil, out BannerScope parsedScope)
    {
        if (!Enum.TryParse(scope, ignoreCase: false, out parsedScope) || !Enum.IsDefined(parsedScope))
            return BannerWriteResult.InvalidScope;

        if (parsedScope == BannerScope.Auction && auctionId is null)
            return BannerWriteResult.MissingAuctionId;

        if (parsedScope != BannerScope.Auction && auctionId is not null)
            return BannerWriteResult.UnexpectedAuctionId;

        if (activeFrom >= activeUntil)
            return BannerWriteResult.InvalidDateRange;

        return null;
    }

    private static BannerPublished ToBannerPublished(Banner banner) => new(
        banner.Id,
        banner.Message,
        banner.Scope.ToString(),
        banner.AuctionId?.ToString(),
        banner.ActiveFrom,
        banner.ActiveUntil);

    private static BannerDto ToDto(Banner banner) => new()
    {
        Id = banner.Id,
        Message = banner.Message,
        Scope = banner.Scope.ToString(),
        AuctionId = banner.AuctionId,
        ActiveFrom = banner.ActiveFrom,
        ActiveUntil = banner.ActiveUntil,
        CreatedBy = banner.CreatedBy
    };
}
