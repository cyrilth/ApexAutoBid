using Mapster;
using MapsterMapper;
using NSubstitute;
using SearchService.Application.DTOs;
using SearchService.Application.Services;
using SearchService.Domain.Entities;
using SearchService.Domain.Enums;
using SearchService.Domain.Interfaces;
using SearchService.Domain.Models;
using Xunit;

namespace SearchService.UnitTests;

/// <summary>
/// Unit tests for <see cref="SearchAppService"/> (Phase 2 Task 9 — coverage areas 9.1–9.7).
/// The unit seam is <see cref="SearchAppService"/> itself: <see cref="IItemRepository"/> is
/// substituted and its captured <see cref="ItemSearchQuery"/> is asserted against, while the
/// real Mapster <c>ItemMappingConfig</c> (scanned exactly like
/// <c>ApplicationServiceExtensions</c> does) is exercised for real so the Item → ItemDto
/// mapping is genuinely pinned, not assumed. Mongo-dependent filter/sort *execution* belongs
/// to the Phase 2 Task 10 integration tests, not here.
/// </summary>
public class SearchAppServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

    // Real Mapster config, scanned from the Application assembly exactly like
    // ApplicationServiceExtensions.AddApplicationServices — so ItemMappingConfig's
    // Item -> ItemDto rule is genuinely exercised, not mocked away.
    private static IMapper BuildMapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(SearchAppService).Assembly);
        return new Mapper(config);
    }

    private static SearchAppService BuildSut(IItemRepository repository, DateTimeOffset? now = null) =>
        new(repository, BuildMapper(), new FixedTimeProvider(now ?? FixedNow));

    private static PagedItems EmptyPagedItems() =>
        new() { Results = [], TotalCount = 0, PageCount = 0 };

    // Every one of Item's 17 fields set to a distinct, assertable value.
    private static Item SampleItem() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc),
        AuctionEnd = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
        Seller = "bob",
        Winner = "alice",
        Make = "Ford",
        Model = "GT",
        Year = 2020,
        Color = "Red",
        Mileage = 12345,
        ImageUrl = "http://images.local/ford-gt.jpg",
        ThumbnailUrl = "http://images.local/ford-gt-thumb.jpg",
        Status = "Live",
        ReservePrice = 10000,
        SoldAmount = 15000,
        CurrentHighBid = 12000
    };

    // ── 9.1 — paged results + full Item -> ItemDto field mapping ────────────────

    [Fact]
    public async Task SearchAsync_WhenSuccessful_MapsAllSeventeenItemFieldsAndSurfacesPagingMetadata()
    {
        var item = SampleItem();
        var paged = new PagedItems
        {
            Results = [item, SampleItem(), SampleItem()],
            TotalCount = 42,
            PageCount = 4
        };
        var repository = Substitute.For<IItemRepository>();
        repository.SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>()).Returns(paged);
        var sut = BuildSut(repository);

        var (outcome, result) = await sut.SearchAsync(new SearchParamsDto(), CancellationToken.None);

        Assert.Equal(SearchOutcome.Success, outcome);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Results.Count);
        Assert.Equal(42, result.TotalCount);
        Assert.Equal(4, result.PageCount);

        var dto = result.Results[0];
        Assert.Equal(item.Id, dto.Id);
        Assert.Equal(item.CreatedAt, dto.CreatedAt);
        Assert.Equal(item.UpdatedAt, dto.UpdatedAt);
        Assert.Equal(item.AuctionEnd, dto.AuctionEnd);
        Assert.Equal(item.Seller, dto.Seller);
        Assert.Equal(item.Winner, dto.Winner);
        Assert.Equal(item.Make, dto.Make);
        Assert.Equal(item.Model, dto.Model);
        Assert.Equal(item.Year, dto.Year);
        Assert.Equal(item.Color, dto.Color);
        Assert.Equal(item.Mileage, dto.Mileage);
        Assert.Equal(item.ImageUrl, dto.ImageUrl);
        Assert.Equal(item.ThumbnailUrl, dto.ThumbnailUrl);
        Assert.Equal(item.Status, dto.Status);
        Assert.Equal(item.ReservePrice, dto.ReservePrice);
        Assert.Equal(item.SoldAmount, dto.SoldAmount);
        Assert.Equal(item.CurrentHighBid, dto.CurrentHighBid);
    }

    [Fact]
    public async Task SearchAsync_WhenPageNumberAndPageSizeOmitted_QueryDefaultsTo1And12()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.PageNumber);
        Assert.Equal(12, captured.PageSize);
    }

    [Fact]
    public async Task SearchAsync_PropagatesTheCallersCancellationTokenToTheRepositoryUnchanged()
    {
        // Every other test in this file uses Arg.Any<CancellationToken>() for the stub setup
        // (it has to, since the token isn't known until the act step) — that would silently
        // let a bug that forwards CancellationToken.None instead of the real token go
        // undetected everywhere else. This test uses a real, distinguishable token and
        // requires the repository to have received EXACTLY that token, not just any token.
        using var cts = new CancellationTokenSource();
        var repository = Substitute.For<IItemRepository>();
        repository.SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto(), cts.Token);

        await repository.Received().SearchAsync(Arg.Any<ItemSearchQuery>(), cts.Token);
    }

    // ── 9.2 — searchTerm passthrough + sanitization ──────────────────────────────

    [Fact]
    public async Task SearchAsync_PassesSearchTermThroughToQuery()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { SearchTerm = "ferrari" }, CancellationToken.None);

        Assert.Equal("ferrari", captured!.SearchTerm);
    }

    [Theory]
    [InlineData("-ford", "ford")]
    [InlineData("-ford -gt", "ford gt")]
    [InlineData("ford -gt", "ford gt")]
    public async Task SearchAsync_WhenSearchTermHasLeadingHyphenTokens_StripsHyphensWithoutExcluding(
        string input, string expected)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { SearchTerm = input }, CancellationToken.None);

        Assert.Equal(expected, captured!.SearchTerm);
    }

    [Theory]
    [InlineData("say \"hello\"", "say hello")]
    [InlineData("\"quoted\"", "quoted")]
    public async Task SearchAsync_WhenSearchTermHasEmbeddedQuotes_StripsQuotes(string input, string expected)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { SearchTerm = input }, CancellationToken.None);

        Assert.Equal(expected, captured!.SearchTerm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_WhenSearchTermWhitespaceOnly_QueryCarriesNullSearchTerm(string? input)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { SearchTerm = input }, CancellationToken.None);

        Assert.Null(captured!.SearchTerm);
    }

    [Fact]
    public async Task SearchAsync_WhenSearchTermOverLong_TruncatesTo200OrFewerCharacters()
    {
        // A non-repeating pattern (not a single repeated character) so the "is a real prefix
        // of the input, not some other substring" assertion below is actually meaningful.
        var input = string.Concat(Enumerable.Range(0, 300).Select(i => (char)('a' + i % 26)));
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { SearchTerm = input }, CancellationToken.None);

        Assert.NotNull(captured!.SearchTerm);
        // 200 is the documented cap (Requirements-level contract for this anon endpoint's
        // defensive length guard), asserted directly rather than mirroring the private
        // implementation constant.
        Assert.True(
            captured.SearchTerm!.Length <= 200,
            "searchTerm forwarded downstream must never exceed 200 characters.");
        Assert.True(
            captured.SearchTerm.Length < input.Length,
            "an over-long searchTerm must actually be truncated, not passed through untouched.");
        Assert.StartsWith(captured.SearchTerm, input);
    }

    // ── 9.3 — seller passthrough ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_PassesSellerThroughToQuery()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { Seller = "bob" }, CancellationToken.None);

        Assert.Equal("bob", captured!.Seller);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_WhenSellerWhitespaceOnly_QueryCarriesNullSeller(string? input)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { Seller = input }, CancellationToken.None);

        Assert.Null(captured!.Seller);
    }

    // ── 9.4 — winner passthrough ───────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_PassesWinnerThroughToQuery()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { Winner = "alice" }, CancellationToken.None);

        Assert.Equal("alice", captured!.Winner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_WhenWinnerWhitespaceOnly_QueryCarriesNullWinner(string? input)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { Winner = input }, CancellationToken.None);

        Assert.Null(captured!.Winner);
    }

    [Fact]
    public async Task SearchAsync_WhenSellerAndWinnerBothSupplied_BothLandOnTheCapturedQuery()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(
            new SearchParamsDto { Seller = "bob", Winner = "alice" }, CancellationToken.None);

        Assert.Equal("bob", captured!.Seller);
        Assert.Equal("alice", captured.Winner);
    }

    // ── 9.5 — sorts by make ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("make")]
    [InlineData("MAKE")]
    [InlineData("Make")]
    public async Task SearchAsync_WhenOrderByMake_QueryCarriesItemOrderByMake(string value)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { OrderBy = value }, CancellationToken.None);

        Assert.Equal(ItemOrderBy.Make, captured!.OrderBy);
    }

    [Fact]
    public async Task SearchAsync_WhenOrderByInvalid_ReturnsInvalidOrderByWithoutCallingRepository()
    {
        var repository = Substitute.For<IItemRepository>();
        var sut = BuildSut(repository);

        var (outcome, result) = await sut.SearchAsync(
            new SearchParamsDto { OrderBy = "bogus" }, CancellationToken.None);

        Assert.Equal(SearchOutcome.InvalidOrderBy, outcome);
        Assert.Null(result);
        await repository.DidNotReceive().SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>());
    }

    // ── 9.6 — sorts by endingSoon (the default) ────────────────────────────────────

    [Theory]
    [InlineData("endingSoon")]
    [InlineData("ENDINGSOON")]
    [InlineData("EndingSoon")]
    public async Task SearchAsync_WhenOrderByEndingSoon_QueryCarriesItemOrderByEndingSoon(string value)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { OrderBy = value }, CancellationToken.None);

        Assert.Equal(ItemOrderBy.EndingSoon, captured!.OrderBy);
    }

    [Fact]
    public async Task SearchAsync_WhenOrderByOmitted_QueryDefaultsToEndingSoon()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { OrderBy = null }, CancellationToken.None);

        Assert.Equal(ItemOrderBy.EndingSoon, captured!.OrderBy);
    }

    // ── 9.7 — filters by status (live, finished, endingSoon) + Now determinism ─────

    [Theory]
    [InlineData("live", ItemFilterBy.Live)]
    [InlineData("LIVE", ItemFilterBy.Live)]
    [InlineData("Live", ItemFilterBy.Live)]
    [InlineData("finished", ItemFilterBy.Finished)]
    [InlineData("FINISHED", ItemFilterBy.Finished)]
    [InlineData("endingSoon", ItemFilterBy.EndingSoon)]
    [InlineData("ENDINGSOON", ItemFilterBy.EndingSoon)]
    [InlineData("all", ItemFilterBy.All)]
    public async Task SearchAsync_WhenFilterByRecognizedValue_QueryCarriesMatchingItemFilterBy(
        string value, ItemFilterBy expected)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { FilterBy = value }, CancellationToken.None);

        Assert.Equal(expected, captured!.FilterBy);
    }

    [Fact]
    public async Task SearchAsync_WhenFilterByOmitted_QueryDefaultsToAll()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        await sut.SearchAsync(new SearchParamsDto { FilterBy = null }, CancellationToken.None);

        Assert.Equal(ItemFilterBy.All, captured!.FilterBy);
    }

    [Fact]
    public async Task SearchAsync_WhenFilterByInvalid_ReturnsInvalidFilterByWithoutCallingRepository()
    {
        var repository = Substitute.For<IItemRepository>();
        var sut = BuildSut(repository);

        var (outcome, result) = await sut.SearchAsync(
            new SearchParamsDto { FilterBy = "bogus" }, CancellationToken.None);

        Assert.Equal(SearchOutcome.InvalidFilterBy, outcome);
        Assert.Null(result);
        await repository.DidNotReceive().SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_QueryNowEqualsInjectedTimeProviderInstantExactly()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository, fixedNow);

        await sut.SearchAsync(new SearchParamsDto(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(fixedNow.UtcDateTime, captured!.Now);
    }

    // ── Validation: pageNumber / pageSize ───────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_WhenPageNumberInvalid_ReturnsInvalidPageNumberWithoutCallingRepository(
        int pageNumber)
    {
        var repository = Substitute.For<IItemRepository>();
        var sut = BuildSut(repository);

        var (outcome, result) = await sut.SearchAsync(
            new SearchParamsDto { PageNumber = pageNumber }, CancellationToken.None);

        Assert.Equal(SearchOutcome.InvalidPageNumber, outcome);
        Assert.Null(result);
        await repository.DidNotReceive().SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SearchAsync_WhenPageSizeInvalid_ReturnsInvalidPageSizeWithoutCallingRepository(int pageSize)
    {
        var repository = Substitute.For<IItemRepository>();
        var sut = BuildSut(repository);

        var (outcome, result) = await sut.SearchAsync(
            new SearchParamsDto { PageSize = pageSize }, CancellationToken.None);

        Assert.Equal(SearchOutcome.InvalidPageSize, outcome);
        Assert.Null(result);
        await repository.DidNotReceive().SearchAsync(Arg.Any<ItemSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public async Task SearchAsync_WhenPageSizeAtBoundary_IsValidAndPassedThroughUnchanged(int pageSize)
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        var (outcome, _) = await sut.SearchAsync(
            new SearchParamsDto { PageSize = pageSize }, CancellationToken.None);

        Assert.Equal(SearchOutcome.Success, outcome);
        Assert.Equal(pageSize, captured!.PageSize);
    }

    [Fact]
    public async Task SearchAsync_WhenPageNumberAtBoundaryOfOne_IsValidAndPassedThroughUnchanged()
    {
        var repository = Substitute.For<IItemRepository>();
        ItemSearchQuery? captured = null;
        repository.SearchAsync(Arg.Do<ItemSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(EmptyPagedItems());
        var sut = BuildSut(repository);

        var (outcome, _) = await sut.SearchAsync(
            new SearchParamsDto { PageNumber = 1 }, CancellationToken.None);

        Assert.Equal(SearchOutcome.Success, outcome);
        Assert.Equal(1, captured!.PageNumber);
    }
}
