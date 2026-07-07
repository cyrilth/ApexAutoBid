using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

/// <summary>
/// Real-time push endpoint at <c>/notifications</c> (Architecture.md §3.3). This hub is
/// server-push only — clients never invoke a method on it — so it declares no methods of its
/// own; every message reaching a connected client originates from one of the event consumers
/// in <c>Consumers/</c>, via <see cref="IHubContext{THub}"/>.
/// </summary>
/// <remarks>
/// Deliberately not decorated with <c>[Authorize]</c> — Phase 6 Task 3.1 / Architecture.md
/// §3.3 requires that anonymous clients can connect and receive broadcasts
/// (<c>AuctionCreated</c>, <c>BidPlaced</c>, <c>AuctionFinished</c>). Authentication is
/// additive, not required: a connection that DOES present a valid JWT (Task 3.2 — the
/// <c>access_token</c> query-string convention wired in Program.cs) is additionally reachable
/// by username via <see cref="Clients.User"/> targeted sends
/// (<see cref="UsernameUserIdProvider"/>), which the <c>AuctionFinished</c> consumer uses to
/// deliver <c>AuctionWon</c>/<c>AuctionSellerResult</c> only to the relevant connections.
/// </remarks>
public class NotificationHub : Hub
{
}
