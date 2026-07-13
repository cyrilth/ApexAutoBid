using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Interfaces;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="PlatformSettingsAppService"/> (Phase 11 Task 3.8/3.9): the
/// DB-override-then-config resolution order, range validation, and AuditEntry writes.
/// </summary>
public class PlatformSettingsServiceTests
{
    private static PlatformSettingsAppService BuildSut(
        IPlatformSettingsRepository repository, AuctionDurationOptions? options = null) =>
        new(repository, Options.Create(options ?? new AuctionDurationOptions()));

    // ── GetEffectiveDurationBoundsAsync — resolution order ───────────────────

    [Fact]
    public async Task GetEffectiveDurationBoundsAsync_WhenNoDbRow_FallsBackToConfig()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        repository.GetAsync().Returns((PlatformSettings?)null);
        var options = new AuctionDurationOptions
        {
            MinDuration = TimeSpan.FromHours(2),
            MaxDuration = TimeSpan.FromDays(30)
        };
        var sut = BuildSut(repository, options);

        var (min, max) = await sut.GetEffectiveDurationBoundsAsync();

        Assert.Equal(TimeSpan.FromHours(2), min);
        Assert.Equal(TimeSpan.FromDays(30), max);
    }

    [Fact]
    public async Task GetEffectiveDurationBoundsAsync_WhenDbRowExists_OverridesConfig()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        repository.GetAsync().Returns(new PlatformSettings
        {
            Id = Guid.NewGuid(),
            MinDuration = TimeSpan.FromMinutes(1),
            MaxDuration = TimeSpan.FromDays(7),
            UpdatedBy = "admin1",
            UpdatedAt = DateTime.UtcNow
        });
        var sut = BuildSut(repository);

        var (min, max) = await sut.GetEffectiveDurationBoundsAsync();

        Assert.Equal(TimeSpan.FromMinutes(1), min);
        Assert.Equal(TimeSpan.FromDays(7), max);
    }

    // ── GetDurationSettingsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetDurationSettingsAsync_WhenNoDbRow_ReturnsConfigDefaultsWithNullMetadata()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        repository.GetAsync().Returns((PlatformSettings?)null);
        var sut = BuildSut(repository);

        var dto = await sut.GetDurationSettingsAsync();

        Assert.Null(dto.UpdatedBy);
        Assert.Null(dto.UpdatedAt);
    }

    // ── UpdateDurationSettingsAsync — validation ─────────────────────────────

    [Fact]
    public async Task UpdateDurationSettingsAsync_WhenMinNotPositive_ReturnsInvalidRange()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        var sut = BuildSut(repository);

        var result = await sut.UpdateDurationSettingsAsync(
            new UpdateDurationSettingsDto { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
            "admin1");

        Assert.Equal(PlatformSettingsWriteResult.InvalidRange, result.Status);
        Assert.Null(result.Settings);
        repository.DidNotReceive().Add(Arg.Any<PlatformSettings>());
    }

    [Fact]
    public async Task UpdateDurationSettingsAsync_WhenMaxNotGreaterThanMin_ReturnsInvalidRange()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        var sut = BuildSut(repository);

        var result = await sut.UpdateDurationSettingsAsync(
            new UpdateDurationSettingsDto
            {
                MinDuration = TimeSpan.FromDays(2),
                MaxDuration = TimeSpan.FromDays(1)
            },
            "admin1");

        Assert.Equal(PlatformSettingsWriteResult.InvalidRange, result.Status);
    }

    // ── UpdateDurationSettingsAsync — success path (insert vs update) ────────

    [Fact]
    public async Task UpdateDurationSettingsAsync_WhenNoExistingRow_InsertsNewRowAndWritesAuditEntry()
    {
        var repository = Substitute.For<IPlatformSettingsRepository>();
        repository.GetAsync().Returns((PlatformSettings?)null);
        var sut = BuildSut(repository);

        var result = await sut.UpdateDurationSettingsAsync(
            new UpdateDurationSettingsDto { MinDuration = TimeSpan.FromMinutes(5), MaxDuration = TimeSpan.FromDays(10) },
            "admin1");

        Assert.Equal(PlatformSettingsWriteResult.Success, result.Status);
        Assert.NotNull(result.Settings);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Settings!.MinDuration);
        Assert.Equal("admin1", result.Settings.UpdatedBy);

        repository.Received(1).Add(Arg.Is<PlatformSettings>(s =>
            s.MinDuration == TimeSpan.FromMinutes(5) && s.UpdatedBy == "admin1"));
        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e =>
            e.Action == "PlatformSettingsUpdated" && e.ActorIsAdmin));
        await repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateDurationSettingsAsync_WhenExistingRow_UpdatesInPlaceWithoutInserting()
    {
        var existing = new PlatformSettings
        {
            Id = Guid.NewGuid(),
            MinDuration = TimeSpan.FromHours(1),
            MaxDuration = TimeSpan.FromDays(90),
            UpdatedBy = "admin1",
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var repository = Substitute.For<IPlatformSettingsRepository>();
        repository.GetAsync().Returns(existing);
        var sut = BuildSut(repository);

        var result = await sut.UpdateDurationSettingsAsync(
            new UpdateDurationSettingsDto { MinDuration = TimeSpan.FromMinutes(2), MaxDuration = TimeSpan.FromDays(5) },
            "admin2");

        Assert.Equal(PlatformSettingsWriteResult.Success, result.Status);
        Assert.Equal(TimeSpan.FromMinutes(2), existing.MinDuration);
        Assert.Equal("admin2", existing.UpdatedBy);
        repository.DidNotReceive().Add(Arg.Any<PlatformSettings>());
    }
}
