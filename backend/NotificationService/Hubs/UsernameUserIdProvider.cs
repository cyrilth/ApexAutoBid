using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

/// <summary>
/// Maps a SignalR connection to <c>Clients.User</c>'s user id by username instead of
/// the default <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> claim (Phase 6
/// Task 3.2).
/// </summary>
/// <remarks>
/// Program.cs configures this service's JWT bearer authentication with
/// <c>NameClaimType = "username"</c> (Architecture.md §5.5, mirrored verbatim from
/// AuctionService.API/BiddingService.API) — that setting makes
/// <c>ClaimsIdentity.Name</c>/<c>HubConnectionContext.User.Identity.Name</c> resolve to the
/// token's <c>username</c> claim, so <see cref="HubConnectionContext.User"/>'s
/// <c>Identity.Name</c> already IS the username for any authenticated connection.
/// <c>AuctionFinished</c>'s <c>Winner</c>/<c>Seller</c> fields are themselves usernames
/// (Contracts/AuctionFinished.cs), so <c>Clients.User(username)</c> in
/// <c>AuctionFinishedConsumer</c> targets exactly the connection(s) authenticated as that
/// username. Anonymous connections have a null <c>User.Identity.Name</c> and are therefore
/// never reachable via <c>Clients.User</c> — only via <c>Clients.All</c> broadcasts, matching
/// Task 3.1's "broadcasts only" anonymous behavior.
/// </remarks>
public class UsernameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.Identity?.Name;
}
