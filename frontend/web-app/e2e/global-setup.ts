import fs from "node:fs";
import { chromium } from "@playwright/test";
import { signInViaIdentityServer } from "./fixtures/auth";
import { chromiumLaunchArgs } from "./fixtures/dev-domains";
import { AUTH_DIR, storageStatePath } from "./fixtures/storage-state";
import { SEEDED_USERS } from "./fixtures/test-data";

/** Same fallback `playwright.config.ts`'s `baseURL`/`fixtures/test.ts`'s `baseURL` resolve to. */
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

/**
 * Runs once, before the whole suite (wired in via `playwright.config.ts`'s `globalSetup`) --
 * NOT a fixture, so it has no `page`/`browser` of its own and drives a plain `chromium.launch()`
 * instead. Signs in each non-admin seeded user (`SEEDED_USERS.bob`/`.alice`/`.tom`) exactly ONCE
 * each, via the real `signInViaIdentityServer` flow (same IdentityServer round-trip
 * `auth.spec.ts` drives per-test), then persists the resulting next-auth session cookie to
 * `e2e/.auth/{username}.json` (`./fixtures/storage-state.ts`).
 *
 * Every Task 15 Batch C spec that needs an already-signed-in user loads one of these files
 * (`test.use({ storageState: storageStatePath(...) })` for a whole spec file, or
 * `browser.newContext({ storageState: ... })` for a single context alongside others in one
 * test) instead of re-driving the login form itself -- the standard Playwright "auth once,
 * reuse everywhere" pattern (https://playwright.dev/docs/auth), and load-bearing here
 * specifically: IdentityService rate-limits `/Account/Login` to 10/60s (Docs/Requirements.md
 * §6), shared across every spec in this run regardless of worker -- Batch A/B's `auth.spec.ts`
 * (3 logins) and `register.spec.ts` (1 registration, which also signs in) already spend some of
 * that budget; Batch C's own bidding/CRUD specs open MANY multi-context pages that all need a
 * signed-in user, and would blow well past 10/60s if each did its own login instead of reusing
 * one of these three files.
 *
 * `admin` is deliberately not signed in here -- no Batch C spec needs an admin session.
 */
export default async function globalSetup(): Promise<void> {
  fs.mkdirSync(AUTH_DIR, { recursive: true });

  // Plain launch() bypasses playwright.config.ts's `use.launchOptions`, so the
  // docker-stack host-resolver mapping (e2e/fixtures/dev-domains.ts) must be
  // passed explicitly here too.
  const browser = await chromium.launch({ args: chromiumLaunchArgs });
  try {
    for (const user of [SEEDED_USERS.bob, SEEDED_USERS.alice, SEEDED_USERS.tom]) {
      const context = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
      const page = await context.newPage();
      await signInViaIdentityServer(page, user);
      await context.storageState({ path: storageStatePath(user.username) });
      await context.close();
    }
  } finally {
    await browser.close();
  }
}
