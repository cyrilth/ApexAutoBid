using System.Diagnostics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Helpers for driving a real <see cref="HubConnection"/> against
/// <see cref="CustomWebAppFactory"/>'s in-memory <c>TestServer</c>, and for awaiting (or
/// asserting the absence of) a pushed message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transport choice:</b> WebSockets over a <c>TestServer</c> require wiring
/// <c>TestServer.CreateWebSocketClient()</c> into the connection's <c>WebSocketFactory</c>, which
/// is fiddly and, in practice, less reliable inside a <c>WebApplicationFactory</c> host than the
/// alternative used here: forcing <see cref="HttpTransportType.LongPolling"/>. Long polling routes
/// every SignalR request (negotiate, poll, send) through plain HTTP, which
/// <c>HttpMessageHandlerFactory</c> — set to <c>factory.Server.CreateHandler()</c> — already
/// handles perfectly, since that's exactly the in-memory handler
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}.CreateClient"/>
/// itself is built on. This is the standard, documented way to unit/integration-test a SignalR
/// hub hosted by <c>WebApplicationFactory</c>.
/// </para>
/// <para>
/// <b>Authentication:</b> whatever <c>AccessTokenProvider</c> returns is sent on every HTTP
/// request the SignalR client makes for the connection — but NOT as the <c>access_token</c>
/// query-string parameter Program.cs's real <c>OnMessageReceived</c> reads the JWT from
/// (that convention is WebSockets/Server-Sent-Events-specific, since browsers can't attach a
/// custom header to those handshakes). For the <see cref="HttpTransportType.LongPolling"/>
/// transport forced here, the .NET SignalR client instead sends it as a normal
/// <c>Authorization: Bearer &lt;token&gt;</c> header — confirmed empirically while building this
/// suite. <see cref="TestAuthHandler"/> reads that header (falling back to the query string) and
/// treats the value as the username verbatim. Passing <c>username: null</c> (the default) omits
/// <c>AccessTokenProvider</c> entirely, producing a genuinely anonymous connection.
/// </para>
/// </remarks>
internal static class SignalRTestHelpers
{
    private static readonly TimeSpan DefaultReceiveTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Builds (but does not start) a <see cref="HubConnection"/> to <c>/notifications</c> on
    /// <paramref name="factory"/>'s in-memory server. Pass <paramref name="username"/> to produce
    /// a connection <see cref="TestAuthHandler"/> authenticates as that username; omit it for an
    /// anonymous connection.
    /// </summary>
    public static HubConnection CreateConnection(CustomWebAppFactory factory, string? username = null)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "notifications"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;

                if (username is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(username);
                }
            })
            .Build();
    }

    /// <summary>
    /// Awaits <paramref name="tcs"/> (completed by a <c>connection.On&lt;T&gt;(...)</c> handler),
    /// failing the test with a clear message if it doesn't complete within
    /// <paramref name="timeout"/> (default 10s) — generous relative to normal RabbitMQ
    /// delivery + SignalR push latency in this suite.
    /// </summary>
    public static async Task<T> WaitForMessageAsync<T>(
        TaskCompletionSource<T> tcs, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultReceiveTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        await using var registration =
            cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert.Fail($"No {typeof(T).Name} message was received within {effectiveTimeout}.");
            throw new UnreachableException(); // Assert.Fail always throws; unreachable in practice.
        }
    }

    /// <summary>
    /// Waits briefly (well short of <see cref="DefaultReceiveTimeout"/>) and asserts
    /// <paramref name="tcs"/> did NOT complete in that window — used to prove a targeted message
    /// (e.g. <c>AuctionWon</c>) was NOT delivered to a connection it shouldn't reach (Task 6.5).
    /// </summary>
    public static async Task AssertNoMessageAsync<T>(
        TaskCompletionSource<T> tcs, CancellationToken cancellationToken, TimeSpan? quietPeriod = null)
    {
        var delay = Task.Delay(quietPeriod ?? DefaultQuietPeriod, cancellationToken);
        var completed = await Task.WhenAny(tcs.Task, delay);

        if (completed == tcs.Task)
        {
            Assert.Fail($"Unexpected {typeof(T).Name} message was received.");
        }
    }
}
