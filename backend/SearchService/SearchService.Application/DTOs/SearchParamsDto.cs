namespace SearchService.Application.DTOs;

/// <summary>
/// Query-string parameters for <c>GET api/search</c> (Requirements §3.2). Bound in the API
/// layer via <c>[FromQuery] SearchParamsDto</c> — ASP.NET Core's complex-type query binder
/// matches properties by name (case-insensitive) against query-string keys, so this stays a
/// plain POCO with public settable properties and zero MVC attributes (Application must not
/// reference ASP.NET Core MVC).
/// </summary>
/// <remarks>
/// Every property is deliberately loose (raw strings, nullable ints) — <c>SearchAppService</c>
/// is solely responsible for validating/normalizing this into a Domain
/// <c>ItemSearchQuery</c> and returning a 400-mappable outcome for anything invalid.
/// </remarks>
public class SearchParamsDto
{
    public string? SearchTerm { get; set; }
    public string? Seller { get; set; }
    public string? Winner { get; set; }

    /// <summary>Raw string — "make" | "new" | "endingSoon" (case-insensitive), or null for the default.</summary>
    public string? OrderBy { get; set; }

    /// <summary>Raw string — "all" | "live" | "endingSoon" | "finished" (case-insensitive), or null for the default.</summary>
    public string? FilterBy { get; set; }

    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
}
