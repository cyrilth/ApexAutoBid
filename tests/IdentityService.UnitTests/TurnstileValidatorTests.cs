using System.Net;
using IdentityService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IdentityService.UnitTests;

/// <summary>
/// Unit tests for <see cref="TurnstileValidator"/> (Phase 3 Task 16.1) against a fake
/// <see cref="HttpMessageHandler"/> — no real network call, no real Cloudflare dependency.
/// Covers exactly what the task asked for: success, failure (with Cloudflare's own
/// <c>error-codes</c> shape), and the missing-token short-circuit (verified as a NO network
/// call, not just a false return — that's the whole point of checking it before calling
/// Cloudflare at all).
/// </summary>
public class TurnstileValidatorTests
{
    private static TurnstileValidator BuildValidator(FakeHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com") },
            Options.Create(new TurnstileOptions
            {
                SiteKey = "1x00000000000000000000AA",
                SecretKey = "1x0000000000000000000000000000000AA",
            }),
            NullLogger<TurnstileValidator>.Instance);

    [Fact]
    public async Task ValidateAsync_SuccessResponse_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"success": true}""");
        var validator = BuildValidator(handler);

        var result = await validator.ValidateAsync("a-real-looking-token", "203.0.113.1", TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ValidateAsync_FailureResponse_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"success": false, "error-codes": ["invalid-input-response"]}""");
        var validator = BuildValidator(handler);

        var result = await validator.ValidateAsync("an-invalid-token", "203.0.113.1", TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_MissingOrEmptyToken_ReturnsFalseWithoutCallingCloudflare(string? token)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"success": true}""");
        var validator = BuildValidator(handler);

        var result = await validator.ValidateAsync(token, "203.0.113.1", TestContext.Current.CancellationToken);

        Assert.False(result);
        // The whole point: never burn a network call on an obviously-empty token.
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ValidateAsync_NetworkFailure_FailsClosed()
    {
        var handler = new FakeHttpMessageHandler(throwOnSend: true);
        var validator = BuildValidator(handler);

        var result = await validator.ValidateAsync("a-token", "203.0.113.1", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    /// <summary>
    /// Minimal scriptable <see cref="HttpMessageHandler"/> — no HTTP mocking package needed for
    /// this one project-local shape (status code + string body, or a thrown exception).
    /// </summary>
    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string? body = null, bool throwOnSend = false)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public FakeHttpMessageHandler(bool throwOnSend) : this(HttpStatusCode.OK, null, throwOnSend)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (throwOnSend)
            {
                throw new HttpRequestException("simulated network failure");
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body ?? string.Empty, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
