import { NextResponse } from "next/server";
import { cookies } from "next/headers";

/**
 * OIDC front-channel logout receiver.
 *
 * Registered as the "webapp" client's `FrontChannelLogoutUri`
 * (backend/IdentityService/Config.cs). Per the OpenID Connect Front-Channel
 * Logout spec, whenever a user's IdentityServer session ends -- whether
 * they signed out from this app, another ApexAutoBid client (e.g. a future
 * mobile app), or IdentityServer's own UI -- Duende loads this URL in a
 * hidden `<iframe>` on the browser for every client that shares that
 * session, passing `iss` and `sid` query params. There's no user-driven
 * navigation and no session/CSRF ceremony here (an iframe GET can't carry
 * that), so we can't reuse next-auth's normal `signOut()` flow; instead we
 * just clear our own session cookie(s) directly so this app's local auth
 * state stays in sync with IdentityServer's.
 *
 * The response body doesn't matter (Duende only cares that the iframe
 * loaded); an empty 200 is exactly what the spec expects.
 */
export async function GET() {
  const cookieStore = await cookies();

  for (const { name } of cookieStore.getAll()) {
    // Matches both the plain and "__Secure-" prefixed variants next-auth
    // uses for the JWT session cookie, including the "<name>.0", "<name>.1"
    // chunks it splits into once the encrypted payload (which carries our
    // access/refresh/id tokens -- see auth.ts) exceeds a single cookie's
    // size limit.
    if (name.startsWith("authjs.session-token") || name.startsWith("__Secure-authjs.session-token")) {
      cookieStore.delete(name);
    }
  }

  return new NextResponse(null, { status: 200 });
}
