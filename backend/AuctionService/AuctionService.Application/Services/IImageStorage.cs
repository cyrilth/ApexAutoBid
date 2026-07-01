namespace AuctionService.Application.Services;

/// <summary>
/// Result of a presigned-upload request: the server-generated key, the short-lived
/// signed PUT URL the client uploads directly to, the final public object URL, and
/// the URL's expiry timestamp (UTC).
/// </summary>
public record PresignedUpload(string Key, string UploadUrl, string ObjectUrl, DateTime ExpiresAt);

/// <summary>
/// Abstraction over the S3-compatible object store (MinIO in dev/self-hosted; any
/// S3-compatible provider in production — see <c>Docs/Requirements.md</c> §8.4). Kept in
/// the Application layer so the image app service and controllers depend only on this
/// interface, never on <c>AWSSDK.S3</c> or any other Infrastructure-layer package.
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Generates a server-side GUID key and a 5-minute presigned PUT URL (Content-Type and
    /// Content-Length signed), plus the final public object URL.
    /// </summary>
    PresignedUpload CreatePresignedUpload(string contentType, long sizeBytes);

    /// <summary>
    /// Downloads the original object (anonymous read), resizes it to a max-400px-wide WebP
    /// thumbnail, uploads it to <c>thumbs/{key}.webp</c>, and returns its public URL. Returns
    /// <see langword="null"/> if the source object is missing.
    /// </summary>
    Task<string?> CreateThumbnailAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// HEADs the public object URL and returns its <c>Content-Length</c>, or <see langword="null"/>
    /// if the object is missing or the length is unknown.
    /// </summary>
    Task<long?> TryGetObjectSizeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an object from the bucket. Reserved for object-lifecycle cleanup (e.g. removing an
    /// auction's images when the auction is deleted — a later enhancement); deliberately NOT called
    /// to remove client-referenced objects that fail create/update size validation, since the
    /// caller's ownership of a referenced key can't be proven (see <c>Docs/Requirements.md</c> §3.1).
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Builds the public URL for an object key: <c>{PublicBaseUrl}/{Bucket}/{key}</c>.</summary>
    string BuildObjectUrl(string key);
}
