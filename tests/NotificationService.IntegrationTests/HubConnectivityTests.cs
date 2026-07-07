using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Phase 6 Task 6.4 — basic hub connectivity: an anonymous client must be able to connect to
/// <c>/notifications</c> and reach <see cref="HubConnectionState.Connected"/>, matching Task 3.1
/// ("<see cref="NotificationHub"/> carries no <c>[Authorize]</c>").
/// </summary>
[Collection(NotificationServiceCollection.Name)]
public class HubConnectivityTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task AnonymousClient_CanConnect_ToNotificationsHub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory);

        await connection.StartAsync(cancellationToken);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task AuthenticatedClient_CanAlsoConnect_ToNotificationsHub()
    {
        // Authentication is additive, not required (Task 3.1) — an authenticated connection must
        // reach the hub exactly like an anonymous one.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = SignalRTestHelpers.CreateConnection(factory, username: "connectivity-dave");

        await connection.StartAsync(cancellationToken);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }
}
