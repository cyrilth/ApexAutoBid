using System.Text.Json;
using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AuctionService.Application.Services;

/// <summary>Application-service implementation of <see cref="IAdminAuctionService"/>.</summary>
public class AdminAuctionAppService(
    IAuctionRepository repository,
    IMapper mapper,
    IPublishEndpoint publishEndpoint,
    ILogger<AdminAuctionAppService> logger) : IAdminAuctionService
{
    public async Task<AdminAuctionWriteResult> EndAuctionAsync(Guid id, string admin)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AdminAuctionWriteResult.NotFound;

        auction.AuctionEnd = DateTime.UtcNow;
        auction.UpdatedAt = DateTime.UtcNow;

        // Map from the updated tracked entity — same rule 6 (AuctionDto -> AuctionUpdated)
        // AuctionAppService.UpdateAuctionAsync reuses, so the item fields (Make/Model/Color/
        // Mileage/Year/ImageUrl/ThumbnailUrl) are populated straight from the entity, not
        // hand-assembled here. Published BEFORE SaveChangesAsync for atomic outbox + domain commit.
        var auctionDto = mapper.Map<AuctionDto>(auction);
        await publishEndpoint.Publish(mapper.Map<AuctionUpdated>(auctionDto));

        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = admin,
            ActorIsAdmin = true,
            Action = "AuctionEndedByAdmin",
            EntityType = "Auction",
            EntityId = auction.Id.ToString(),
            Data = JsonSerializer.Serialize(new { auction.Id, auction.AuctionEnd })
        });

        await repository.SaveChangesAsync();

        logger.LogInformation("Auction {AuctionId} ended immediately by admin {Admin}", id, admin);
        return AdminAuctionWriteResult.Success;
    }

    public async Task<AdminAuctionWriteResult> CancelAuctionAsync(Guid id, string admin)
    {
        var auction = await repository.GetByIdAsync(id);
        if (auction is null)
            return AdminAuctionWriteResult.NotFound;

        auction.Status = Status.Cancelled;
        auction.UpdatedAt = DateTime.UtcNow;

        await publishEndpoint.Publish(new AuctionCancelled(auction.Id.ToString(), auction.Seller));

        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = admin,
            ActorIsAdmin = true,
            Action = "AuctionCancelledByAdmin",
            EntityType = "Auction",
            EntityId = auction.Id.ToString(),
            Data = JsonSerializer.Serialize(new { auction.Id, auction.Seller })
        });

        await repository.SaveChangesAsync();

        logger.LogInformation("Auction {AuctionId} cancelled by admin {Admin}", id, admin);
        return AdminAuctionWriteResult.Success;
    }

    public async Task<AuctionStatsDto> GetStatsAsync()
    {
        var counts = await repository.GetStatusCountsAsync();

        var byStatus = Enum.GetValues<Status>()
            .ToDictionary(s => s.ToString(), s => counts.GetValueOrDefault(s));

        return new AuctionStatsDto
        {
            Total = counts.Values.Sum(),
            ByStatus = byStatus
        };
    }
}
