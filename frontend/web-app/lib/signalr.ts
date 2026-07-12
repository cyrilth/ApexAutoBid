"use client";

import { HttpTransportType, HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";

/**
 * Browser-facing base URL for the Gateway (Architecture.md §3.5). Distinct from the
 * server-only `GATEWAY_URL` every `lib/*-service.ts`/`lib/*-actions.ts` module uses (those run
 * in the Next.js Node process, never in the browser) -- `HubConnection` runs IN the browser
 * (`components/NotificationProvider.tsx`), so it needs a URL the browser itself can resolve.
 * `NEXT_PUBLIC_` is Next.js's convention for env vars inlined into the client bundle at build
 * time (see `.env`'s comment on this same var for the LAN/remote caveat).
 */
const NOTIFICATIONS_HUB_URL = `${process.env.NEXT_PUBLIC_GATEWAY_URL ?? "http://localhost:6001"}/notifications`;

/**
 * Fetches the current user's own IdentityServer access token from the session-gated
 * `GET /api/auth/hub-token` Route Handler (see that file's remarks for why this one
 * browser-facing exception to the "tokens are server-only" rule is safe). Used as the
 * HubConnection's `accessTokenFactory` -- SignalR calls this itself once on connect AND again
 * on every automatic reconnect, so a refreshed token (auth.ts's background rotation) is picked
 * up without the frontend having to do anything extra.
 *
 * Never throws: returns `""` on any non-200 response or network failure, which
 * NotificationService's `OnMessageReceived` treats identically to no token at all
 * (`if (!string.IsNullOrEmpty(accessToken))`), so a transient failure here degrades to an
 * anonymous connection rather than breaking the connection outright.
 */
async function fetchHubToken(): Promise<string> {
  try {
    const res = await fetch("/api/auth/hub-token", { cache: "no-store" });
    if (!res.ok) return "";
    const { accessToken } = (await res.json()) as { accessToken?: string };
    return accessToken ?? "";
  } catch {
    return "";
  }
}

/**
 * Builds (but does not start) the app's one SignalR connection to NotificationHub (Phase 7
 * Task 8.2/8.3). `NotificationProvider` owns the single instance's lifecycle; this factory is
 * just the construction logic, kept independent of React so it stays easy to read/reason about
 * on its own.
 *
 * `skipNegotiation` + WebSockets-only transport: GatewayService (Architecture.md §3.5) has no
 * CORS policy configured -- every other browser-facing data flow in this app runs server-side
 * (Next.js Route Handlers/Server Actions/Server Components calling the Gateway from Node, never
 * the browser), so cross-origin browser requests to the Gateway have simply never come up
 * before this feature, and adding CORS there is out of scope (backend is not to be modified for
 * this task). SignalR's default HTTP-based negotiation opens with a cross-origin
 * `POST .../notifications/negotiate` XHR, which the browser blocks without CORS response
 * headers the Gateway doesn't send. A raw WebSocket upgrade request, by contrast, is NOT
 * subject to the browser's CORS enforcement (only `fetch`/`XHR` are) -- ASP.NET Core accepts it
 * with no CORS middleware needed, and GatewayService's own `appsettings.json` "notifications"
 * route comment already documents that YARP passes WebSocket upgrades through unchanged.
 * Forcing `transport: HttpTransportType.WebSockets` with `skipNegotiation: true` connects
 * directly via `ws(s)://.../notifications`, bypassing the negotiate call -- and the CORS
 * problem -- entirely. Trade-off: no automatic fallback to Server-Sent Events/long-polling if a
 * client's network genuinely blocks WebSockets; acceptable here since every environment this
 * app targets (dev, Docker Compose, Kubernetes) supports them.
 *
 * `accessTokenFactory` is only attached when `withToken` is true -- an anonymous visitor's
 * connection skips it entirely (a real "connects anonymously" connection, not merely one whose
 * token factory happens to return ""), matching NotificationHub's own "broadcasts only for
 * anonymous connections" contract. `NotificationProvider` passes the signed-in state as of
 * mount/rebuild; see its own remarks on when/why it rebuilds this connection.
 */
export function createNotificationConnection(withToken: boolean): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(NOTIFICATIONS_HUB_URL, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      ...(withToken ? { accessTokenFactory: fetchHubToken } : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
