using AuctionService.Application.DTOs;

namespace AuctionService.Application.Services;

/// <summary>Outcome of a <c>upload-url</c> request, so the controller can map it to an HTTP status.</summary>
public enum UploadUrlOutcome
{
    Success,
    InvalidContentType,

    /// <summary>
    /// The declared size failed validation — either non-positive or above the per-image limit.
    /// Named for the validation rule rather than "TooLarge" since a zero/negative size is invalid
    /// but not oversized; the controller maps this to a 400 "Invalid file size" response.
    /// </summary>
    InvalidSize
}

/// <summary>Outcome of a <c>thumbnail</c> request, so the controller can map it to an HTTP status.</summary>
public enum ThumbnailOutcome
{
    Success,
    InvalidKey,
    SourceNotFound
}

/// <summary>
/// Application-level service for auction image operations (presigned uploads and thumbnail
/// generation — Phase 1 Task 18). Controllers depend only on this interface — never on
/// <see cref="IImageStorage"/> or any Infrastructure type.
/// </summary>
public interface IAuctionImageService
{
    /// <summary>
    /// Validates the requested content type and declared size, then issues a presigned upload
    /// URL. Returns <see cref="UploadUrlOutcome.InvalidContentType"/> or
    /// <see cref="UploadUrlOutcome.InvalidSize"/> on validation failure.
    /// </summary>
    Task<(UploadUrlOutcome Outcome, UploadUrlResponse? Response)> CreateUploadUrlAsync(UploadUrlRequest request);

    /// <summary>
    /// Generates a WebP thumbnail for a previously uploaded object key. Returns
    /// <see cref="ThumbnailOutcome.InvalidKey"/> when <paramref name="key"/> is not a bare GUID
    /// (SSRF guard) or <see cref="ThumbnailOutcome.SourceNotFound"/> when the source object
    /// does not exist.
    /// </summary>
    Task<(ThumbnailOutcome Outcome, ThumbnailResponse? Response)> CreateThumbnailAsync(
        string key,
        CancellationToken ct = default);
}
