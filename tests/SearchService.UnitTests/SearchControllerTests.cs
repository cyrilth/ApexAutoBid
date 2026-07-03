using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SearchService.API.Controllers;
using SearchService.Application.DTOs;
using SearchService.Application.Services;
using Xunit;

namespace SearchService.UnitTests;

/// <summary>
/// Unit tests for <see cref="SearchController"/> (Phase 2 Task 9): asserts the controller
/// maps every <see cref="SearchOutcome"/> to the correct HTTP result. Mirrors
/// AuctionsControllerTests' substitute-the-service-and-assert-the-mapping style.
/// </summary>
public class SearchControllerTests
{
    private readonly ISearchService _service = Substitute.For<ISearchService>();

    private SearchController BuildController() =>
        new(_service, NullLogger<SearchController>.Instance);

    private static SearchResultDto SampleResult() =>
        new() { Results = [], TotalCount = 3, PageCount = 1 };

    [Fact]
    public async Task Search_WhenSuccessful_Returns200WithTheServiceResult()
    {
        var dto = SampleResult();
        _service.SearchAsync(Arg.Any<SearchParamsDto>(), Arg.Any<CancellationToken>())
            .Returns((SearchOutcome.Success, dto));
        var controller = BuildController();

        var result = await controller.Search(new SearchParamsDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(dto, ok.Value);
    }

    [Fact]
    public async Task Search_WhenPageNumberInvalid_Returns400WithMatchingProblemDetails()
    {
        _service.SearchAsync(Arg.Any<SearchParamsDto>(), Arg.Any<CancellationToken>())
            .Returns((SearchOutcome.InvalidPageNumber, (SearchResultDto?)null));
        var controller = BuildController();

        var result = await controller.Search(new SearchParamsDto(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid pageNumber", problem.Title);
        Assert.Equal("pageNumber must be 1 or greater.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task Search_WhenPageSizeInvalid_Returns400WithMatchingProblemDetails()
    {
        _service.SearchAsync(Arg.Any<SearchParamsDto>(), Arg.Any<CancellationToken>())
            .Returns((SearchOutcome.InvalidPageSize, (SearchResultDto?)null));
        var controller = BuildController();

        var result = await controller.Search(new SearchParamsDto(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid pageSize", problem.Title);
        Assert.Equal("pageSize must be between 1 and 50.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task Search_WhenOrderByInvalid_Returns400WithMatchingProblemDetails()
    {
        _service.SearchAsync(Arg.Any<SearchParamsDto>(), Arg.Any<CancellationToken>())
            .Returns((SearchOutcome.InvalidOrderBy, (SearchResultDto?)null));
        var controller = BuildController();

        var result = await controller.Search(new SearchParamsDto(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid orderBy", problem.Title);
        Assert.Equal("orderBy must be one of: make, new, endingSoon.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task Search_WhenFilterByInvalid_Returns400WithMatchingProblemDetails()
    {
        _service.SearchAsync(Arg.Any<SearchParamsDto>(), Arg.Any<CancellationToken>())
            .Returns((SearchOutcome.InvalidFilterBy, (SearchResultDto?)null));
        var controller = BuildController();

        var result = await controller.Search(new SearchParamsDto(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid filterBy", problem.Title);
        Assert.Equal("filterBy must be one of: all, live, endingSoon, finished.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }
}
