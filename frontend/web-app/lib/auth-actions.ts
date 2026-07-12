"use server";

import { redirect } from "next/navigation";
import { signIn, signOut, identityServerIssuer, identityServerPostLogoutRedirectUri } from "@/auth";
import { getIdToken } from "@/lib/auth-token";

/**
 * Redirects straight to IdentityServer's login page for the "identityserver"
 * provider. Kept zero-argument (rather than taking an optional callback URL)
 * so it stays assignable to a plain `<form action={...}>`, which React
 * requires to accept a `FormData` first parameter -- see `signInReturningTo`
 * below for the redirect-with-return-path variant.
 */
export async function signInWithIdentityServer(): Promise<void> {
  await signIn("identityserver");
}

/**
 * Same sign-in redirect as `signInWithIdentityServer`, but for callers that
 * need the user back on a specific page afterwards rather than wherever
 * they came from (next-auth's `redirectTo` -- otherwise it falls back to
 * the referring page). Not a `<form action>` -- invoked directly (e.g. from
 * a Server Component's render body when it detects a signed-out visitor).
 * Used by the create/edit auction pages (Phase 7 Task 6) so a signed-out
 * visitor lands back on the exact form they were trying to reach.
 */
export async function signInReturningTo(callbackUrl: string): Promise<void> {
  await signIn("identityserver", { redirectTo: callbackUrl });
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
