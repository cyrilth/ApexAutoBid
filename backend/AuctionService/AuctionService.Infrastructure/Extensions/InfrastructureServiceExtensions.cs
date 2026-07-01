using Amazon.Runtime;
using Amazon.S3;
using AuctionService.Application.Configuration;
using AuctionService.Application.Services;
using AuctionService.Domain.Interfaces;
using AuctionService.Infrastructure.Data;
using AuctionService.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuctionService.Infrastructure.Extensions;

/// <summary>
/// Registers all Infrastructure-layer services into the DI container.
/// Call <c>builder.Services.AddInfrastructureServices(builder.Configuration)</c>
/// from the API's <c>Program.cs</c>.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AuctionDbContext>(opt =>
            opt.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IAuctionRepository, AuctionRepository>();

        // ── Image storage (Phase 1 Task 18) ──────────────────────────────────────
        //
        // ImagesOptions/MinioOptions are bound here so the Application layer stays
        // free of any Microsoft.Extensions.Options wiring concerns; the options
        // classes themselves live in Application/Configuration so both layers (and
        // AuctionAppService's gallery-enforcement logic) can depend on them.

        services.Configure<ImagesOptions>(configuration.GetSection(ImagesOptions.SectionName));
        services.Configure<MinioOptions>(configuration.GetSection(MinioOptions.SectionName));

        // IAmazonS3 is registered as a singleton (the AWS SDK client is thread-safe
        // and expensive to construct) using the Auction Service's dedicated,
        // least-privilege MinIO access key (PutObject/DeleteObject on
        // auction-images/* only — see docker/minio/auction-svc-policy.json).
        // ForcePathStyle=true is required for MinIO (and most non-AWS S3-compatible
        // providers), which serve buckets at {endpoint}/{bucket}/{key} rather than
        // the AWS-style {bucket}.{endpoint}/{key} virtual-hosted addressing.
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<MinioOptions>>().Value;

            return new AmazonS3Client(
                new BasicAWSCredentials(o.AccessKey, o.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = o.ServiceUrl,
                    ForcePathStyle = true,
                    AuthenticationRegion = o.Region
                });
        });

        // Typed HttpClient for MinioImageStorage's anonymous-read paths (thumbnail
        // download, HEAD size checks) — deliberately separate from the S3 SDK, which
        // this key cannot use for GET/HEAD (see IImageStorage remarks).
        services.AddHttpClient<IImageStorage, MinioImageStorage>();

        return services;
    }
}
