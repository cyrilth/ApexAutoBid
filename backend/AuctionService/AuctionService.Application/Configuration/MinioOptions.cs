using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.Configuration;

/// <summary>
/// Binds the <c>Minio</c> configuration section — the credentials and endpoint the
/// Auction Service uses to talk to its dedicated, least-privilege MinIO access key
/// (<c>PutObject</c> + <c>DeleteObject</c> on <c>auction-images/*</c> only; see
/// <c>docker/minio/auction-svc-policy.json</c> and <c>Docs/Requirements.md</c> §3.1/§8.4).
/// <para>
/// <c>AccessKey</c>/<c>SecretKey</c> are dev-only committed values in
/// <c>appsettings.Development.json</c>; production credentials are supplied via
/// environment variables only (never committed — see <c>Docs/Requirements.md</c> §6).
/// </para>
/// </summary>
public class MinioOptions
{
    /// <summary>Configuration section name bound from <c>appsettings*.json</c>.</summary>
    public const string SectionName = "Minio";

    /// <summary>The S3-compatible API endpoint, e.g. <c>http://localhost:9000</c> in dev.</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>Access key for the Auction Service's dedicated MinIO account.</summary>
    [Required]
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>Secret key for the Auction Service's dedicated MinIO account.</summary>
    [Required]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// AWS SDK signing region. MinIO does not enforce regions, but the SDK requires one
    /// to be present to compute the SigV4 signature — any value works as long as it is
    /// consistent between requests.
    /// </summary>
    [Required]
    public string Region { get; init; } = "us-east-1";
}
