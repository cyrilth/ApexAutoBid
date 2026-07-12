import { NextResponse } from "next/server";
import { auth } from "@/auth";
import { getAccessToken } from "@/lib/auth-token";

/**
 * The ONE sanctioned exception to this app's "tokens are server-only" rule
 * (`auth.ts`'s `session` callback deliberately omits accessToken/refreshToken/idToken from what
 * reaches the browser; `lib/auth-token.ts` is `server-only` for exactly that reason). SignalR's
 * `HubConnection` runs IN the browser (`components/NotificationProvider.tsx`,
 * `lib/signalr.ts`), and NotificationHub's targeted "AuctionWon"/"AuctionSellerResult" sends
 * (Phase 6 Task 3.2/4.4, `UsernameUserIdProvider`) only reach a connection that presents the
 * caller's own JWT on the WebSocket handshake -- there is no way to get that token onto the
 * connection without the browser holding it, however briefly, for `accessTokenFactory` to hand
 * over.
 *
 * Why this is safe: this route returns ONLY the calling session's OWN access token -- the exact
 * same one every other server-side caller of `getAccessToken()` (lib/auction-service.ts,
 * lib/bid-actions.ts, ...) already sends to the Gateway on this same user's behalf. A signed-in
 * user retrieving their own access token from an endpoint gated on their own (httpOnly,
 * encrypted) session cookie is equivalent to what a plain, non-httpOnly cookie would already
 * expose to that same browser -- it grants no capability beyond what this user's session
 * already has, and nothing here lets one user retrieve another user's token. The refresh token
 * NEVER leaves the server: this route only ever calls `getAccessToken()`, never
 * `getIdToken()`/the raw JWT, matching every other caller of lib/auth-token.ts.
 *
 * `Cache-Control: no-store` on EVERY response (success or 401) -- this reflects live session
 * state and must never be served from a shared/browser cache: a cached 401 would wrongly strand
 * a user who just signed in, and a cached access token would go stale the moment auth.ts's
 * background rotation refreshes it.
 */
export async function GET() {
  const session = await auth();

  if (!session?.user?.username) {
    return NextResponse.json(
      { error: "Not signed in." },
      { status: 401, headers: { "Cache-Control": "no-store" } }
    );
  }

  const accessToken = await getAccessToken();

  if (!accessToken) {
    // Should be unreachable in practice -- a session carrying a `username` claim always came
    // from a successful token exchange (auth.ts's `jwt` callback) -- but fail closed rather
    // than hand back an empty/undefined token if it ever happens (e.g. mid refresh-failure,
    // session.error === "RefreshTokenError").
    return NextResponse.json(
      { error: "No access token available." },
      { status: 401, headers: { "Cache-Control": "no-store" } }
    );
  }

  return NextResponse.json(
    { accessToken },
    { status: 200, headers: { "Cache-Control": "no-store" } }
  );
}
