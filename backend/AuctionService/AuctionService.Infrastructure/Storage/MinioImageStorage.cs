using AuctionService.Application.Configuration;
using AuctionService.Application.Services;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace AuctionService.Infrastructure.Storage;

/// <summary>
/// <see cref="IImageStorage"/> implementation backed by MinIO (or any S3-compatible provider
/// in production) via <c>AWSSDK.S3</c> for writes/presigning and a plain <see cref="HttpClient"/>
/// for reads. Reads (thumbnail download, HEAD size checks) deliberately bypass the S3 SDK and go
/// through the bucket's anonymous read policy over plain HTTP — the Auction Service's dedicated
/// MinIO key only has <c>PutObject</c>/<c>DeleteObject</c> rights on <c>auction-images/*</c>
/// (see <c>docker/minio/auction-svc-policy.json</c> and <c>Docs/Requirements.md</c> §3.1/§8.4).
/// </summary>
public class MinioImageStorage(
    IAmazonS3 s3,
    HttpClient http,
    IOptions<ImagesOptions> imagesOptions,
    IOptions<MinioOptions> minioOptions,
    ILogger<MinioImageStorage> logger) : IImageStorage
{
    private const int ThumbnailMaxWidth = 400;
    private const string ThumbnailContentType = "image/webp";

    // Restricts decoding to the same content types accepted at upload time (§3.1) — a
    // Configuration built with only these modules registered means ImageSharp will never
    // even attempt to sniff/decode any other format, on top of the try/catch below.
    private static readonly Configuration SupportedFormatsConfiguration = new(
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new WebpConfigurationModule());

    public PresignedUpload CreatePresignedUpload(string contentType, long sizeBytes)
    {
        var key = Guid.NewGuid().ToString();
        var expires = DateTime.UtcNow.AddMinutes(5);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = imagesOptions.Value.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expires,
            ContentType = contentType
        };

        // Signs Content-Length into the presigned URL so a client that uploads a
        // different-sized file than declared has the PUT rejected by the object store
        // (defense in depth alongside the declared-size check — see §3.1).
        request.Headers.ContentLength = sizeBytes;

        // GetPreSignedURL always renders an https:// URL regardless of the configured
        // ServiceUrl scheme. Dev MinIO speaks plain HTTP, so the scheme must be set
        // explicitly from MinioOptions.ServiceUrl or the client's PUT can't connect.
        request.Protocol = minioOptions.Value.ServiceUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? Amazon.S3.Protocol.HTTPS
            : Amazon.S3.Protocol.HTTP;

        var uploadUrl = s3.GetPreSignedURL(request);

        logger.LogInformation(
            "Issued presigned upload URL for object key {Key} against {ServiceUrl}, expiring {ExpiresAt}",
            key, minioOptions.Value.ServiceUrl, expires);

        return new PresignedUpload(key, uploadUrl, BuildObjectUrl(key), expires);
    }

    public async Task<string?> CreateThumbnailAsync(string key, CancellationToken cancellationToken = default)
    {
        var sourceUrl = BuildObjectUrl(key);
        using var response = await http.GetAsync(sourceUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Thumbnail source object {Key} not found (status {StatusCode})",
                key, response.StatusCode);
            return null;
        }

        await using var thumbnailStream = new MemoryStream();

        try
        {
            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var decoderOptions = new DecoderOptions { Configuration = SupportedFormatsConfiguration };
            using var image = await Image.LoadAsync(decoderOptions, sourceStream, cancellationToken);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbnailMaxWidth, 0)
            }));

            await image.SaveAsWebpAsync(thumbnailStream, cancellationToken);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            // Malformed or unsupported source object — reject gracefully rather than 500.
            // The caller (AuctionImageAppService) maps a null return to ThumbnailOutcome.SourceNotFound.
            logger.LogWarning(ex,
                "Thumbnail source object {Key} could not be decoded as a supported image format", key);
            return null;
        }

        thumbnailStream.Position = 0;

        var thumbnailKey = $"thumbs/{key}.webp";

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = imagesOptions.Value.Bucket,
            Key = thumbnailKey,
            InputStream = thumbnailStream,
            ContentType = ThumbnailContentType,
            AutoCloseStream = false
        }, cancellationToken);

        logger.LogInformation("Generated thumbnail {ThumbnailKey} from source object {Key}", thumbnailKey, key);

        return BuildObjectUrl(thumbnailKey);
    }

    public async Task<long?> TryGetObjectSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, BuildObjectUrl(key));
        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("HEAD check for object {Key} failed with status {StatusCode}",
                key, response.StatusCode);
            return null;
        }

        return response.Content.Headers.ContentLength;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await s3.DeleteObjectAsync(imagesOptions.Value.Bucket, key, cancellationToken);

        logger.LogInformation("Deleted object {Key} from bucket {Bucket}", key, imagesOptions.Value.Bucket);
    }

    public string BuildObjectUrl(string key) =>
        $"{imagesOptions.Value.PublicBaseUrl.TrimEnd('/')}/{imagesOptions.Value.Bucket}/{key}";
}
