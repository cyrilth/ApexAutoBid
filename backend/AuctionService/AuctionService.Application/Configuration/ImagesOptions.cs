namespace AuctionService.Application.Configuration;

/// <summary>
/// Binds the <c>Images</c> configuration section — controls presigned upload
/// validation, the public URL prefix used to recognise platform-hosted images,
/// and the per-auction gallery bounds enforced by <c>AuctionAppService</c>.
/// <para>
/// <c>PublicBaseUrl</c> and the MinIO credentials (<see cref="MinioOptions"/>) are
/// environment-specific and therefore live only in <c>appsettings.Development.json</c>
/// / environment variables — never hardcoded here (see <c>Docs/Requirements.md</c> §6).
/// </para>
/// </summary>
public class ImagesOptions
{
    /// <summary>Configuration section name bound from <c>appsettings*.json</c>.</summary>
    public const string SectionName = "Images";

    /// <summary>
    /// The public base URL images are served from, e.g. <c>http://localhost:9000</c> in dev
    /// or the production S3-compatible endpoint. Combined with <see cref="Bucket"/> and an
    /// object key to build/recognise platform-hosted image URLs.
    /// </summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    /// <summary>The bucket auction images are stored in.</summary>
    public string Bucket { get; init; } = "auction-images";

    /// <summary>Maximum allowed size, in megabytes, for a single uploaded image.</summary>
    public int MaxSizeMB { get; init; } = 5;

    /// <summary>Maximum number of images allowed in a single auction's gallery.</summary>
    public int MaxPerAuction { get; init; } = 10;
}
