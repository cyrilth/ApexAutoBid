"use server";

import { redirect } from "next/navigation";
import { signIn, signOut, identityServerIssuer, identityServerPostLogoutRedirectUri } from "@/auth";
import { getIdToken } from "@/lib/auth-token";

/** Redirects straight to IdentityServer's login page for the "identityserver" provider. */
export async function signInWithIdentityServer(): Promise<void> {
  await signIn("identityserver");
}

/**
 * RP-initiated (federated) sign-out: clears this app's own session, then
 * sends the browser to Duende's `/connect/endsession` endpoint so the
 * IdentityServer-side session ends too -- a plain `signOut()` would only
 * clear our cookie and leave the user silently still logged in at
 * IdentityServer (and any other client sharing that session).
 *
 * Ending the IdentityServer session is also what triggers Duende to notify
 * every *other* client sharing it via their own front-channel logout
 * receivers (our receiver for that direction is
 * app/signout-oidc/route.ts).
 */
export async function signOutFederated(): Promise<void> {
  const idToken = await getIdToken();

  // redirect: false -- we do our own redirect below, straight to
  // IdentityServer rather than back into this app.
  await signOut({ redirect: false });

  const params = new URLSearchParams({
    post_logout_redirect_uri: identityServerPostLogoutRedirectUri,
  });
  if (idToken) {
    params.set("id_token_hint", idToken);
  }

  redirect(`${identityServerIssuer}/connect/endsession?${params.toString()}`);
}
