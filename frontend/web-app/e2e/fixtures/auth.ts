import type { Page } from "@playwright/test";

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
 * TODO(Task 15.6): implement once the login spec is written.
 *  1. Trigger sign-in the same way a user would (e.g. `page.goto("/session")`
 *     then click "Sign in") so next-auth's `signIn("identity-server")`
 *     redirects the browser to IdentityServer's `/Account/Login` page
 *     (`AUTH_IDENTITYSERVER_ISSUER` in `.env`).
 *  2. Fill IdentityServer's own login form -- selectors come from
 *     `backend/IdentityService`'s hosted Razor Pages (Username/Password
 *     inputs + submit button on `/Account/Login`), not this repo's
 *     component tree.
 *  3. Wait for the redirect back to `baseURL` (see `./test.ts`) once
 *     IdentityServer completes the Authorization Code flow and next-auth
 *     sets its session cookie.
 *  4. Assert the page reflects a signed-in state (today: the "Sign out"
 *     button on `app/session/page.tsx`; update once real nav/login UI
 *     replaces that harness).
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
  throw new Error(
    `signInViaIdentityServer() is a Phase 7 Task 14 scaffold stub and is not ` +
      `implemented yet -- see the TODO above this function (Task 15.6). ` +
      `Received credentials for user "${credentials.username}".`,
  );
}
