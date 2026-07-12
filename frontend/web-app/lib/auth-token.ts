import "server-only";
import { headers } from "next/headers";
import { getToken } from "next-auth/jwt";

/**
 * Server-only access to the raw IdentityServer tokens carried inside the
 * encrypted next-auth session cookie (populated by the `jwt` callback in
 * `auth.ts`). These are deliberately excluded from the `session` callback,
 * so `useSession()` / `GET /api/auth/session` never see them -- this module
 * (and anything that imports it) is the only path to the real access/id
 * token, and the `server-only` import makes it a build error to
 * accidentally pull this into a Client Component bundle.
 *
 * Use `getAccessToken()` from Server Components, Route Handlers, or Server
 * Actions that need to call the Gateway on the signed-in user's behalf
 * (future tasks -- e.g. `Authorization: Bearer ${await getAccessToken()}`).
 */
async function getAuthToken() {
  return getToken({
    req: { headers: await headers() },
    secret: process.env.AUTH_SECRET,
    // Must match how the cookie was actually set: unprefixed in dev
    // (plain http://localhost:3000), "__Secure-" prefixed once this app is
    // served over TLS in production (Docs/Requirements.md §6 -- Nginx +
    // acme-companion).
    secureCookie: process.env.NODE_ENV === "production",
  });
}

/** The current user's IdentityServer access token, or `undefined` if signed out. */
export async function getAccessToken(): Promise<string | undefined> {
  const token = await getAuthToken();
  return token?.accessToken;
}

/** The current user's IdentityServer id_token -- needed as `id_token_hint` for RP-initiated (federated) logout. */
export async function getIdToken(): Promise<string | undefined> {
  const token = await getAuthToken();
  return token?.idToken;
}

/**
 * True once the background refresh in auth.ts's `jwt` callback has given up
 * (refresh token expired, revoked, or reuse-after-rotation detected).
 * Equivalent to checking `session.error === "RefreshTokenError"` from
 * `auth()`, but usable anywhere this module already is.
 */
export async function hasRefreshTokenError(): Promise<boolean> {
  const token = await getAuthToken();
  return token?.error === "RefreshTokenError";
}
