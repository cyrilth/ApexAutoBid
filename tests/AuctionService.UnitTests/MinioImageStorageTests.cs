using Amazon.S3;
using Amazon.S3.Model;
using AuctionService.Application.Configuration;
using AuctionService.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Unit tests for <see cref="MinioImageStorage.CreatePresignedUpload"/> (Phase 1 Task 18.3).
/// Only the presign path is exercised — it never touches the network, so a plain
/// <see cref="HttpClient"/> (used only by the read paths — thumbnail download and HEAD size
/// checks) is safe to construct directly without any stubbing.
/// </summary>
public class MinioImageStorageTests
{
    private static MinioImageStorage BuildSut(IAmazonS3 s3) => new(
        s3,
        new HttpClient(),
        Options.Create(new ImagesOptions
        {
            PublicBaseUrl = "http://localhost:9000",
            Bucket = "auction-images",
            MaxSizeMB = 5,
            MaxPerAuction = 10
        }),
        Options.Create(new MinioOptions
        {
            ServiceUrl = "http://localhost:9000",
            AccessKey = "k",
            SecretKey = "s",
            Region = "us-east-1"
        }),
        NullLogger<MinioImageStorage>.Instance);

    [Fact]
    public void CreatePresignedUpload_ReturnsGuidKeyAndFutureExpiryAndObjectUrl()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("http://minio/presigned");
        var sut = BuildSut(s3);

        var result = sut.CreatePresignedUpload("image/jpeg", 1_000);

        Assert.True(Guid.TryParse(result.Key, out _));
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("http://localhost:9000/auction-images/" + result.Key, result.ObjectUrl);
    }

    // Regression test for the critical fix: GetPreSignedURL always renders an https:// URL
    // regardless of the configured ServiceUrl scheme, so the Protocol must be set explicitly
    // from MinioOptions.ServiceUrl or a plain-HTTP dev MinIO PUT can't connect.
    [Fact]
    public void CreatePresignedUpload_WhenServiceUrlIsHttp_SetsRequestProtocolToHttp()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://minio/presigned");
        GetPreSignedUrlRequest? captured = null;
        s3.When(x => x.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()))
            .Do(callInfo => captured = callInfo.Arg<GetPreSignedUrlRequest>());
        var sut = BuildSut(s3);

        sut.CreatePresignedUpload("image/jpeg", 1_000);

        Assert.NotNull(captured);
        Assert.Equal(Amazon.S3.Protocol.HTTP, captured!.Protocol);
    }
}
