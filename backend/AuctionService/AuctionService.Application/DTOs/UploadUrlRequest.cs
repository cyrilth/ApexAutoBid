using System.ComponentModel.DataAnnotations;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Input DTO for <c>POST api/auctions/upload-url</c>.
/// <para>
/// The caller declares the content type and file size <b>before</b> uploading any bytes.
/// The Auction Service validates both against the configured whitelist/limit (see
/// <c>AuctionImageAppService</c>) and, on success, signs the declared <c>SizeBytes</c> into
/// the presigned PUT URL's <c>Content-Length</c> — so a client that lies about the size and
/// then PUTs a different-sized file will have the upload rejected by MinIO/S3 itself
/// (defense in depth, see <c>Docs/Requirements.md</c> §3.1).
/// </para>
/// </summary>
public class UploadUrlRequest
{
    /// <summary>MIME type of the file to upload. Must be one of image/jpeg, image/png, image/webp.</summary>
    [Required]
    public required string ContentType { get; init; }

    /// <summary>Declared file size in bytes. Must be greater than zero and within <c>Images:MaxSizeMB</c>.</summary>
    public long SizeBytes { get; init; }
}
