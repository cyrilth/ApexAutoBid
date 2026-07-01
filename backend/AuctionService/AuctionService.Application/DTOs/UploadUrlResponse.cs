namespace AuctionService.Application.DTOs;

/// <summary>
/// Response DTO for <c>POST api/auctions/upload-url</c>. The client PUTs the raw file bytes
/// to <see cref="UploadUrl"/> directly against object storage (bytes never flow through the
/// Auction Service) and then uses <see cref="ObjectUrl"/> as the image's <c>Url</c> when it
/// later submits the create/update auction form.
/// </summary>
public class UploadUrlResponse
{
    /// <summary>Server-generated GUID object key (no user-controlled paths, no overwrites).</summary>
    public required string Key { get; init; }

    /// <summary>The presigned PUT URL. Expires 5 minutes after issuance.</summary>
    public required string UploadUrl { get; init; }

    /// <summary>The final public URL the uploaded object will be reachable at once the PUT succeeds.</summary>
    public required string ObjectUrl { get; init; }

    /// <summary>UTC expiry timestamp of <see cref="UploadUrl"/>.</summary>
    public DateTime ExpiresAt { get; init; }
}
