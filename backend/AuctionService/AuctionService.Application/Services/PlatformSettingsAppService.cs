using System.Text.Json;
using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace AuctionService.Application.Services;

/// <summary>Application-service implementation of <see cref="IPlatformSettingsService"/>.</summary>
public class PlatformSettingsAppService(
    IPlatformSettingsRepository repository,
    IOptions<AuctionDurationOptions> options) : IPlatformSettingsService
{
    public async Task<(TimeSpan MinDuration, TimeSpan MaxDuration)> GetEffectiveDurationBoundsAsync()
    {
        var settings = await repository.GetAsync();

        return settings is not null
            ? (settings.MinDuration, settings.MaxDuration)
            : (options.Value.MinDuration, options.Value.MaxDuration);
    }

    public async Task<PlatformSettingsDto> GetDurationSettingsAsync()
    {
        var settings = await repository.GetAsync();

        return settings is not null
            ? new PlatformSettingsDto
            {
                MinDuration = settings.MinDuration,
                MaxDuration = settings.MaxDuration,
                UpdatedBy = settings.UpdatedBy,
                UpdatedAt = settings.UpdatedAt
            }
            : new PlatformSettingsDto
            {
                MinDuration = options.Value.MinDuration,
                MaxDuration = options.Value.MaxDuration,
                UpdatedBy = null,
                UpdatedAt = null
            };
    }

    public async Task<PlatformSettingsUpdateResult> UpdateDurationSettingsAsync(
        UpdateDurationSettingsDto dto, string updatedBy)
    {
        if (dto.MinDuration <= TimeSpan.Zero || dto.MaxDuration <= dto.MinDuration)
            return new PlatformSettingsUpdateResult(PlatformSettingsWriteResult.InvalidRange, null);

        var existing = await repository.GetAsync();

        if (existing is null)
        {
            existing = new PlatformSettings
            {
                Id = Guid.NewGuid(),
                MinDuration = dto.MinDuration,
                MaxDuration = dto.MaxDuration,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTime.UtcNow
            };
            repository.Add(existing);
        }
        else
        {
            // existing came from the tracked DbContext query inside repository.GetAsync() —
            // mutating it in place and calling SaveChangesAsync below persists the change via
            // EF Core's change tracker, the same pattern AuctionAppService.UpdateAuctionAsync
            // uses for the tracked Auction entity.
            existing.MinDuration = dto.MinDuration;
            existing.MaxDuration = dto.MaxDuration;
            existing.UpdatedBy = updatedBy;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        // Append-only audit record (Requirements §13.3) — added to the context BEFORE
        // SaveChangesAsync so it commits in the SAME transaction as the settings upsert.
        repository.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Actor = updatedBy,
            ActorIsAdmin = true,
            Action = "PlatformSettingsUpdated",
            EntityType = "PlatformSettings",
            EntityId = existing.Id.ToString(),
            Data = JsonSerializer.Serialize(new { existing.MinDuration, existing.MaxDuration })
        });

        // As with UpdateAuctionAsync, SaveChangesAsync's affected-row count is not used to
        // determine success: resubmitting identical bounds is still a logical success (EF
        // simply detects no dirty columns). Genuine failures throw and are handled globally.
        await repository.SaveChangesAsync();

        var resultDto = new PlatformSettingsDto
        {
            MinDuration = existing.MinDuration,
            MaxDuration = existing.MaxDuration,
            UpdatedBy = existing.UpdatedBy,
            UpdatedAt = existing.UpdatedAt
        };

        return new PlatformSettingsUpdateResult(PlatformSettingsWriteResult.Success, resultDto);
    }
}
