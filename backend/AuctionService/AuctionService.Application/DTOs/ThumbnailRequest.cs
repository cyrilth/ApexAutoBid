using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>POST api/auctions/thumbnail</c>.
/// <para>
/// <b>SSRF guard:</b> <see cref="Key"/> must be a bare GUID — the exact format
/// <c>upload-url</c> issues. URLs, paths, or any other shape are rejected before the
/// service ever attempts to fetch the object, preventing the thumbnail generator from
/// being used to fetch arbitrary internal/external URLs.
/// </para>
/// </summary>
public class ThumbnailRequest
{
    /// <summary>The GUID object key returned by a prior <c>upload-url</c> call.</summary>
    [Required]
    public required string Key { get; init; }
}
