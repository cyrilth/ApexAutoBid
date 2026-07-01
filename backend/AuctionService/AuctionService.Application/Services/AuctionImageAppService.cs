using AuctionService.Application.Configuration;
using AuctionService.Application.DTOs;
using Microsoft.Extensions.Options;

namespace AuctionService.Application.Services;

/// <summary>
/// Application-service implementation backing the presigned-upload and thumbnail-generation
/// endpoints (Phase 1 Task 18). Delegates the actual object-storage interaction to
/// <see cref="IImageStorage"/> and owns only the request-validation rules described in
/// <c>Docs/Requirements.md</c> §3.1.
/// </summary>
public class AuctionImageAppService(
    IImageStorage storage,
    IOptions<ImagesOptions> imagesOptions) : IAuctionImageService
{
    // Whitelist of accepted content types for uploaded auction images (§3.1).
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public Task<(UploadUrlOutcome Outcome, UploadUrlResponse? Response)> CreateUploadUrlAsync(
        UploadUrlRequest request)
    {
        if (!AllowedContentTypes.Contains(request.ContentType))
        {
            return Task.FromResult<(UploadUrlOutcome, UploadUrlResponse?)>(
                (UploadUrlOutcome.InvalidContentType, null));
        }

        var maxBytes = (long)imagesOptions.Value.MaxSizeMB * 1024 * 1024;
        if (request.SizeBytes <= 0 || request.SizeBytes > maxBytes)
        {
            return Task.FromResult<(UploadUrlOutcome, UploadUrlResponse?)>(
                (UploadUrlOutcome.InvalidSize, null));
        }

        var presigned = storage.CreatePresignedUpload(request.ContentType, request.SizeBytes);

        var response = new UploadUrlResponse
        {
            Key = presigned.Key,
            UploadUrl = presigned.UploadUrl,
            ObjectUrl = presigned.ObjectUrl,
            ExpiresAt = presigned.ExpiresAt
        };

        return Task.FromResult<(UploadUrlOutcome, UploadUrlResponse?)>((UploadUrlOutcome.Success, response));
    }

    public async Task<(ThumbnailOutcome Outcome, ThumbnailResponse? Response)> CreateThumbnailAsync(
        string key,
        CancellationToken ct = default)
    {
        // SSRF guard: only a bare GUID (the exact format upload-url issues) is accepted.
        // Anything else — a URL, a path, a key with a prefix like "thumbs/" — is rejected
        // before the storage layer ever attempts to fetch it. Forward the canonical
        // Guid.ToString() form (not the raw input) so a wrapped/whitespace-padded GUID
        // variant that still parses can't diverge from the real object key.
        if (!Guid.TryParse(key, out var parsed))
            return (ThumbnailOutcome.InvalidKey, null);

        var thumbnailUrl = await storage.CreateThumbnailAsync(parsed.ToString(), ct);
        if (thumbnailUrl is null)
            return (ThumbnailOutcome.SourceNotFound, null);

        return (ThumbnailOutcome.Success, new ThumbnailResponse { ThumbnailUrl = thumbnailUrl });
    }
}
