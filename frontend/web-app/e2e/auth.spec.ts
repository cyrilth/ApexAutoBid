import { test, expect, baseURL } from "./fixtures/test";
import { identityServerIssuer, signInViaIdentityServer } from "./fixtures/auth";
import { SEEDED_USERS } from "./fixtures/test-data";

/** Escapes an origin string for safe interpolation into a `RegExp` (e.g. as an anchored prefix). */
function escapeForRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Phase 7 Task 15.6-15.8 -- Auth flows against the LIVE stack: IdentityServer's own hosted
 * Razor Pages login UI (not anything in this Next.js app), federated (RP-initiated) logout, and
 * the unauthenticated create-page gate. See `./fixtures/auth.ts` for the shared
 * `signInViaIdentityServer` driver these (and later Batch C specs) use.
 */
test.describe("auth", () => {
  test("15.6 -- signing in via IdentityServer's login page lands back on this app, signed in", async ({
    page,
  }) => {
    await signInViaIdentityServer(page, SEEDED_USERS.bob);

    // app/session/page.tsx's harness (the only sign-in entry point that exists as of Task 15) --
    // renders "Signed in as {username}" and a "Sign out" button once next-auth's session
    // reflects the completed Authorization Code flow.
    await expect(page).toHaveURL(`${baseURL}/session`);
    await expect(page.getByText(`Signed in as ${SEEDED_USERS.bob.username}`)).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    // The pre-sign-in "Sign in" button must be gone, not just an additional "Sign out" button
    // alongside it.
    await expect(page.getByRole("button", { name: "Sign in" })).toHaveCount(0);
  });

  test("15.7 -- logout ends the session and returns to the app", async ({ page }) => {
    await signInViaIdentityServer(page, SEEDED_USERS.alice);
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();

    // lib/auth-actions.ts's signOutFederated: clears this app's own session, then does an
    // RP-initiated logout against IdentityServer's /connect/endsession.
    await page.getByRole("button", { name: "Sign out" }).click();

    // Duende's Logout/Index.cshtml.cs -- LogoutOptions.ShowLogoutPrompt is true and
    // AutomaticRedirectAfterSignOut is false (backend/IdentityService/Pages/Account/Logout/
    // LogoutOptions.cs), so the flow lands on a "You are now logged out" interstitial with a
    // manual link back to this app (PostLogoutRedirectUri) rather than auto-redirecting.
    await page.waitForURL(new RegExp(`^${escapeForRegExp(identityServerIssuer)}/Account/Logout/LoggedOut`));
    await expect(page.getByText("You are now logged out")).toBeVisible();

    const returnLink = page.getByRole("link", { name: "here" });
    await expect(returnLink).toBeVisible();
    await returnLink.click();

    // Config.cs's "webapp" client PostLogoutRedirectUris -- AUTH_URL (`http://localhost:3000`,
    // this app's home page).
    await expect(page).toHaveURL(`${baseURL}/`);

    // The session is actually gone, not just the browser having navigated somewhere -- re-check
    // the harness page shows the signed-out state again.
    await page.goto("/session");
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toHaveCount(0);
  });

  test("15.8 -- an unauthenticated visitor cannot reach the create-auction page", async ({ page }) => {
    // Fresh, cookie-less browser context (Playwright's default per-test isolation) -- no session
    // exists, so app/auctions/create/page.tsx's server-side auth gate must redirect before the
    // form ever renders.
    await page.goto("/auctions/create");

    // app/auth/signin/route.ts -> next-auth's signIn("identityserver", { redirectTo:
    // "/auctions/create" }) -> IdentityServer's own /Account/Login, carrying a ReturnUrl that
    // (once authenticated) sends the user back to exactly the page they tried to reach.
    await expect(page).toHaveURL(new RegExp(`^${escapeForRegExp(identityServerIssuer)}/Account/Login`));
    await expect(page.getByRole("heading", { level: 1, name: "Login" })).toBeVisible();

    // The create form itself never rendered.
    await expect(page.getByRole("heading", { level: 1, name: "Create auction" })).toHaveCount(0);
    await expect(page.getByLabel("Make")).toHaveCount(0);

    // Signing in now completes the original journey, landing on the create page rather than
    // wherever a bare sign-in (no callbackUrl) would have gone -- proving the gate's
    // `redirect("/auth/signin?callbackUrl=%2Fauctions%2Fcreate")` round-trips correctly.
    await page.getByLabel("Username", { exact: true }).fill(SEEDED_USERS.tom.username);
    await page.getByLabel("Password", { exact: true }).fill(SEEDED_USERS.tom.password);
    await page.getByRole("button", { name: "Login", exact: true }).click();

    await expect(page).toHaveURL(`${baseURL}/auctions/create`);
    await expect(page.getByRole("heading", { level: 1, name: "Create auction" })).toBeVisible();
  });
});
