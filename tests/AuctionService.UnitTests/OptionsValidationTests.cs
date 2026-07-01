using AuctionService.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuctionService.UnitTests;

/// <summary>
/// Verifies the DataAnnotations rules on <see cref="ImagesOptions"/>/<see cref="MinioOptions"/>
/// that Program.cs enforces via <c>ValidateDataAnnotations().ValidateOnStart()</c>. These lock in
/// fail-fast behaviour: a misconfigured environment (e.g. an empty <c>Images:PublicBaseUrl</c>,
/// which would otherwise produce non-absolute object URLs and break platform-hosted image
/// detection) must throw at options-resolution time rather than degrade silently at runtime.
/// The binding here mirrors Program.cs (Bind + ValidateDataAnnotations); resolving
/// <c>IOptions&lt;T&gt;.Value</c> triggers the same validation ValidateOnStart runs at startup.
/// </summary>
public class OptionsValidationTests
{
    private static T Resolve<T>(string section, Dictionary<string, string?> settings) where T : class
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddOptions<T>().Bind(config.GetSection(section)).ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<T>>().Value;
    }

    [Fact]
    public void ImagesOptions_WhenPublicBaseUrlMissing_ThrowsOnResolve()
    {
        var ex = Assert.Throws<OptionsValidationException>(() =>
            Resolve<ImagesOptions>(ImagesOptions.SectionName, new Dictionary<string, string?>
            {
                ["Images:Bucket"] = "auction-images",
                // PublicBaseUrl intentionally omitted → [Required]/[Url] fail.
            }));

        Assert.Contains(nameof(ImagesOptions.PublicBaseUrl), ex.Message);
    }

    [Fact]
    public void ImagesOptions_WhenPublicBaseUrlNotAbsoluteUrl_ThrowsOnResolve()
    {
        Assert.Throws<OptionsValidationException>(() =>
            Resolve<ImagesOptions>(ImagesOptions.SectionName, new Dictionary<string, string?>
            {
                ["Images:PublicBaseUrl"] = "not-a-url",
                ["Images:Bucket"] = "auction-images",
            }));
    }

    [Fact]
    public void ImagesOptions_WhenValid_Resolves()
    {
        var options = Resolve<ImagesOptions>(ImagesOptions.SectionName, new Dictionary<string, string?>
        {
            ["Images:PublicBaseUrl"] = "http://localhost:9000",
            ["Images:Bucket"] = "auction-images",
        });

        Assert.Equal("http://localhost:9000", options.PublicBaseUrl);
    }

    [Fact]
    public void MinioOptions_WhenCredentialsMissing_ThrowsOnResolve()
    {
        Assert.Throws<OptionsValidationException>(() =>
            Resolve<MinioOptions>(MinioOptions.SectionName, new Dictionary<string, string?>
            {
                ["Minio:ServiceUrl"] = "http://localhost:9000",
                // AccessKey/SecretKey intentionally omitted → [Required] fails.
            }));
    }
}
