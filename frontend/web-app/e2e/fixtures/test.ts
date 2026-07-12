import { test as base, expect } from "@playwright/test";

/**
 * Shared Playwright fixtures for every e2e spec (Phase 7 Task 14 scaffold).
 * Import `test`/`expect` from THIS module in specs -- never straight from
 * `@playwright/test` -- so fixtures added here (e.g. an `authenticatedPage`
 * fixture once Task 15.6's login flow is implemented in `./auth.ts`) apply
 * to every spec without touching existing files.
 */

/**
 * Same value `playwright.config.ts`'s `use.baseURL` resolves to
 * (`PLAYWRIGHT_BASE_URL`, default `http://localhost:3000`). Relative
 * `page.goto("/...")` calls already resolve against `use.baseURL`
 * automatically -- this export exists for the few places that need the
 * origin as a plain string outside of `page.goto`, e.g. asserting the
 * browser has landed back on this app (not IdentityServer) after an OIDC
 * redirect round-trip (see `signInViaIdentityServer` in `./auth.ts`).
 */
export const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

// No custom fixtures yet -- `.extend({})` is the documented no-op starting
// point so later Task 15 work (e.g. an `authenticatedPage` fixture wrapping
// `signInViaIdentityServer`) is a pure addition here rather than a rename
// every spec file has to pick up.
export const test = base.extend({});

export { expect };
