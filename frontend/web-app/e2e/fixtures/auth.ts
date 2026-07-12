import type { Page } from "@playwright/test";
import { baseURL } from "./test";

/** Same fallback `auth.ts`'s `identityServerIssuer` uses -- `.env` sets the same value, but this
 * Node-side Playwright process doesn't load `.env` itself, so the fallback is what actually
 * applies unless `AUTH_IDENTITYSERVER_ISSUER` is exported into the shell running Playwright. */
export const identityServerIssuer = process.env.AUTH_IDENTITYSERVER_ISSUER ?? "https://localhost:5001";

/**
 * Credentials for a seeded IdentityService user -- see `./test-data.ts` for
 * the fixed, well-known accounts each environment's DB seed creates.
 */
export interface IdentityServerCredentials {
  username: string;
  password: string;
}

/**
 * Drives IdentityServer's own hosted Razor Pages login UI -- NOT anything in
 * this Next.js app -- via the OIDC Authorization Code redirect that
 * `signInWithIdentityServer` (lib/auth-actions.ts) kicks off when a user
 * clicks "Sign in" (see app/session/page.tsx today; the real nav/login
 * entry point lands in a later Phase 7 task).
 *
 * Needed by Docs/Tasks.md Phase 7 Task 15.6 (login flow) and, transitively,
 * by 15.7 (logout), 15.8 (unauthenticated create-page gate), 15.9–15.11
 * (create/edit/delete auction), 15.13 (place bid), and 15.14 (real-time bid
 * from a second signed-in browser context) -- anything that needs an
 * authenticated `page` before it can act.
 *
 * Implemented for Task 15.6: goes through `/session`'s harness "Sign in"
 * button, fills IdentityServer's own `/Account/Login` form, and waits for
 * the browser to land back on this app's origin once the Authorization Code
 * flow + next-auth's callback complete. Callers assert the resulting
 * signed-in state themselves (today: the "Sign out" button on
 * `app/session/page.tsx`; update once real nav/login UI replaces that
 * harness) since the exact landing page depends on how sign-in was
 * triggered (this harness vs. `signInReturningTo`'s callback URL).
 *
 * Dev registration (Task 15.16) additionally needs Cloudflare Turnstile's
 * official always-pass test site key/secret (Docs/Requirements.md §6) --
 * already the committed dev default, so no extra fixture plumbing is
 * required for that to work end-to-end.
 */
export async function signInViaIdentityServer(
  page: Page,
  credentials: IdentityServerCredentials,
): Promise<void> {
  // Same nav a real visitor uses (app/session/page.tsx's harness form,
  // lib/auth-actions.ts's signInWithIdentityServer server action) --
  // triggers next-auth's `signIn("identityserver")`, which redirects the
  // browser to IdentityServer's own `/connect/authorize` -> `/Account/Login`.
  await page.goto("/session");
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.waitForURL(`${identityServerIssuer}/Account/Login**`);

  // backend/IdentityService/Pages/Account/Login/Index.cshtml -- asp-for
  // generates id="Input_Username"/id="Input_Password" with a <label> whose
  // text is the bare property name (InputModel has no [Display] overrides),
  // so `getByLabel` resolves both by their visible label text.
  await page.getByLabel("Username", { exact: true }).fill(credentials.username);
  await page.getByLabel("Password", { exact: true }).fill(credentials.password);
  // Two submit buttons share name="Input.Button" (values "login"/"cancel")
  // but distinct visible text ("Login"/"Cancel") -- `exact` avoids also
  // matching the page's <h1>Login</h1>.
  await page.getByRole("button", { name: "Login", exact: true }).click();

  // Round-trips back through IdentityServer's /connect/authorize/callback
  // and this app's own /api/auth/callback/identityserver once the
  // Authorization Code flow completes, landing back on this app's origin
  // (exact path depends on next-auth's redirectTo -- callers assert the
  // specific landing page themselves).
  await page.waitForURL((url) => url.origin === new URL(baseURL).origin);
}
