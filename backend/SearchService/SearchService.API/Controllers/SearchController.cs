using Microsoft.AspNetCore.Mvc;
using SearchService.Application.DTOs;
using SearchService.Application.Services;

namespace SearchService.API.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(ISearchService service, ILogger<SearchController> logger) : ControllerBase
{
    // ── 5. GET api/search ─────────────────────────────────────────────────────
    //
    // Anonymous (Requirements §3.2) — this service has no authentication middleware wired
    // up at all yet (Phase 2 Task 12 notes the whole API is anonymous-only). All parameters
    // are optional query-string values bound via complex-type [FromQuery] binding onto
    // SearchParamsDto (ASP.NET Core matches its settable properties by name, case-
    // insensitively, against query-string keys).
    //
    // Validation failures (unknown orderBy/filterBy keyword, out-of-range pageNumber/
    // pageSize) are mapped to 400 ProblemDetails inline here, matching AuctionsController's
    // pattern — a global IExceptionHandler for uncaught exceptions lands in Phase 2 Task 13.
    //
    // Note: a non-numeric pageNumber/pageSize (e.g. ?pageNumber=abc) never reaches
    // SearchAppService at all — [ApiController]'s automatic model-state validation
    // short-circuits complex-type query binding on a type mismatch and returns ASP.NET's own
    // generic ValidationProblemDetails before this action runs. SearchOutcome.InvalidPageNumber/
    // InvalidPageSize only cover in-range-type-but-out-of-range values (e.g. pageSize=0 or
    // 999) — this is accepted, not a gap to close here.

    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] SearchParamsDto request, CancellationToken cancellationToken)
    {
        var (outcome, result) = await service.SearchAsync(request, cancellationToken);

        if (outcome == SearchOutcome.Success)
        {
            // LogDebug only, and never at Information+: request.SearchTerm is anonymous user
            // input and must not be logged at a level enabled by default in production.
            logger.LogDebug(
                "Search returned {ResultCount} of {TotalCount} total results",
                result!.Results.Count, result.TotalCount);
        }

        return outcome switch
        {
            SearchOutcome.InvalidPageNumber => BadRequest(new ProblemDetails
            {
                Title = "Invalid pageNumber",
                Detail = "pageNumber must be 1 or greater.",
                Status = StatusCodes.Status400BadRequest
            }),
            SearchOutcome.InvalidPageSize => BadRequest(new ProblemDetails
            {
                Title = "Invalid pageSize",
                Detail = "pageSize must be between 1 and 50.",
                Status = StatusCodes.Status400BadRequest
            }),
            SearchOutcome.InvalidOrderBy => BadRequest(new ProblemDetails
            {
                Title = "Invalid orderBy",
                Detail = "orderBy must be one of: make, new, endingSoon.",
                Status = StatusCodes.Status400BadRequest
            }),
            SearchOutcome.InvalidFilterBy => BadRequest(new ProblemDetails
            {
                Title = "Invalid filterBy",
                Detail = "filterBy must be one of: all, live, endingSoon, finished.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => Ok(result)
        };
    }
}
