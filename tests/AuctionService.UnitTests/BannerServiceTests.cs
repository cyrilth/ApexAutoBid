using AuctionService.Application.DTOs;
using AuctionService.Application.Services;
using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using AuctionService.Domain.Interfaces;
using Contracts;
using MassTransit;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="BannerAppService"/> (Phase 11 Task 3.5/3.9): scope/date-range
/// validation, BannerPublished emission on create/update (not delete), and AuditEntry writes.
/// </summary>
public class BannerServiceTests
{
    private static CreateBannerDto SampleCreateDto(
        string scope = "Global", Guid? auctionId = null) => new()
    {
        Message = "Sale extended!",
        Scope = scope,
        AuctionId = auctionId,
        ActiveFrom = DateTime.UtcNow,
        ActiveUntil = DateTime.UtcNow.AddDays(1)
    };

    private static BannerAppService BuildSut(IBannerRepository repository, IPublishEndpoint? publishEndpoint = null) =>
        new(repository, publishEndpoint ?? Substitute.For<IPublishEndpoint>());

    // ── Create — validation ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithInvalidScope_ReturnsInvalidScope()
    {
        var repository = Substitute.For<IBannerRepository>();
        var sut = BuildSut(repository);

        var result = await sut.CreateAsync(SampleCreateDto(scope: "Bogus"), "admin1");

        Assert.Equal(BannerWriteResult.InvalidScope, result.Status);
        Assert.Null(result.Banner);
        repository.DidNotReceive().Add(Arg.Any<Banner>());
    }

    [Fact]
    public async Task CreateAsync_WithAuctionScopeAndNoAuctionId_ReturnsMissingAuctionId()
    {
        var repository = Substitute.For<IBannerRepository>();
        var sut = BuildSut(repository);

        var result = await sut.CreateAsync(SampleCreateDto(scope: "Auction", auctionId: null), "admin1");

        Assert.Equal(BannerWriteResult.MissingAuctionId, result.Status);
    }

    [Fact]
    public async Task CreateAsync_WithGlobalScopeAndAuctionId_ReturnsUnexpectedAuctionId()
    {
        var repository = Substitute.For<IBannerRepository>();
        var sut = BuildSut(repository);

        var result = await sut.CreateAsync(SampleCreateDto(scope: "Global", auctionId: Guid.NewGuid()), "admin1");

        Assert.Equal(BannerWriteResult.UnexpectedAuctionId, result.Status);
    }

    [Fact]
    public async Task CreateAsync_WithActiveFromNotBeforeActiveUntil_ReturnsInvalidDateRange()
    {
        var repository = Substitute.For<IBannerRepository>();
        var sut = BuildSut(repository);
        var dto = new CreateBannerDto
        {
            Message = "x",
            Scope = "Global",
            ActiveFrom = DateTime.UtcNow.AddDays(1),
            ActiveUntil = DateTime.UtcNow
        };

        var result = await sut.CreateAsync(dto, "admin1");

        Assert.Equal(BannerWriteResult.InvalidDateRange, result.Status);
    }

    // ── Create — success path ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhenValid_EmitsBannerPublishedAndWritesAuditEntry()
    {
        var repository = Substitute.For<IBannerRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint);

        var result = await sut.CreateAsync(SampleCreateDto(), "admin1");

        Assert.Equal(BannerWriteResult.Success, result.Status);
        Assert.NotNull(result.Banner);
        Assert.Equal("Global", result.Banner!.Scope);
        Assert.Equal("admin1", result.Banner.CreatedBy);

        await publishEndpoint.Received(1).Publish(
            Arg.Is<BannerPublished>(e => e.Message == "Sale extended!" && e.Scope == "Global"),
            Arg.Any<CancellationToken>());

        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e =>
            e.Action == "BannerCreated" && e.EntityType == "Banner" && e.Actor == "admin1" && e.ActorIsAdmin));
        await repository.Received(1).SaveChangesAsync();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ReturnsNotFound()
    {
        var repository = Substitute.For<IBannerRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Banner?)null);
        var sut = BuildSut(repository);

        var result = await sut.UpdateAsync(Guid.NewGuid(), new UpdateBannerDto
        {
            Message = "x",
            Scope = "Global",
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(1)
        }, "admin1");

        Assert.Equal(BannerWriteResult.NotFound, result);
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_EmitsBannerPublishedAndWritesAuditEntry()
    {
        var banner = new Banner
        {
            Id = Guid.NewGuid(),
            Message = "old",
            Scope = BannerScope.HomePage,
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin1"
        };
        var repository = Substitute.For<IBannerRepository>();
        repository.GetByIdAsync(banner.Id).Returns(banner);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint);

        var result = await sut.UpdateAsync(banner.Id, new UpdateBannerDto
        {
            Message = "new message",
            Scope = "Global",
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(2)
        }, "admin2");

        Assert.Equal(BannerWriteResult.Success, result);
        Assert.Equal("new message", banner.Message);
        Assert.Equal(BannerScope.Global, banner.Scope);

        await publishEndpoint.Received(1).Publish(
            Arg.Is<BannerPublished>(e => e.Message == "new message"), Arg.Any<CancellationToken>());
        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e =>
            e.Action == "BannerUpdated" && e.Actor == "admin2" && e.ActorIsAdmin));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsNotFound()
    {
        var repository = Substitute.For<IBannerRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Banner?)null);
        var sut = BuildSut(repository);

        var result = await sut.DeleteAsync(Guid.NewGuid(), "admin1");

        Assert.Equal(BannerWriteResult.NotFound, result);
    }

    [Fact]
    public async Task DeleteAsync_WhenFound_RemovesAndWritesAuditEntryWithoutPublishing()
    {
        var banner = new Banner
        {
            Id = Guid.NewGuid(),
            Message = "bye",
            Scope = BannerScope.Global,
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin1"
        };
        var repository = Substitute.For<IBannerRepository>();
        repository.GetByIdAsync(banner.Id).Returns(banner);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = BuildSut(repository, publishEndpoint);

        var result = await sut.DeleteAsync(banner.Id, "admin1");

        Assert.Equal(BannerWriteResult.Success, result);
        repository.Received(1).Remove(banner);
        repository.Received(1).AddAudit(Arg.Is<AuditEntry>(e => e.Action == "BannerDeleted"));
        await publishEndpoint.DidNotReceive().Publish(Arg.Any<BannerPublished>(), Arg.Any<CancellationToken>());
    }

    // ── GetActiveAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_WithUnrecognizedScope_ReturnsEmptyListWithoutQueryingRepository()
    {
        var repository = Substitute.For<IBannerRepository>();
        var sut = BuildSut(repository);

        var result = await sut.GetActiveAsync("NotAScope", null);

        Assert.Empty(result);
        await repository.DidNotReceive().GetActiveAsync(Arg.Any<BannerScope?>(), Arg.Any<Guid?>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetActiveAsync_WithRecognizedScope_PassesParsedScopeToRepository()
    {
        var repository = Substitute.For<IBannerRepository>();
        repository.GetActiveAsync(BannerScope.HomePage, null, Arg.Any<DateTime>()).Returns([]);
        var sut = BuildSut(repository);

        await sut.GetActiveAsync("HomePage", null);

        await repository.Received(1).GetActiveAsync(BannerScope.HomePage, null, Arg.Any<DateTime>());
    }
}
