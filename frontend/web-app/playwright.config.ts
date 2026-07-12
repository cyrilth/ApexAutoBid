import { defineConfig, devices } from "@playwright/test";

/**
 * Phase 7 Task 14 — Playwright test project scaffold. Task 15 (18 subtasks,
 * Docs/Tasks.md) lands the actual specs on top of this; see `e2e/README.md`
 * (directory scaffold below) for how each subtask is expected to slot in.
 *
 * Run with the `npm run test:e2e*` scripts (package.json) -- the
 * `playwright-tester` sub-agent (Docs/AgentGuide.md) drives those, not this
 * file directly.
 */

/** Same default `AUTH_URL`/site origin the app itself falls back to (lib/site-url.ts). */
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

export default defineConfig({
  // Keeps e2e specs out of any future unit/component test runner's glob
  // (none exists yet) and out of Next's own TS build -- see tsconfig.json's
  // `exclude` and e2e/tsconfig.json.
  testDir: "./e2e",
  testMatch: "**/*.spec.ts",

  // Task 15 Batch C: signs bob/alice/tom in ONCE each via the real IdentityServer flow and
  // persists their next-auth session cookies to `e2e/.auth/*.json` -- every spec that needs a
  // signed-in user loads one of those files (`storageStatePath`, `./e2e/fixtures/storage-
  // state.ts`) instead of re-driving the login form itself. See `./e2e/global-setup.ts`'s own
  // remarks for why this matters beyond just speed (IdentityService's Login rate limit).
  globalSetup: "./e2e/global-setup.ts",

  // A single flaky retry locally still fails fast; CI (once Phase 10 wires
  // this in) gets a couple of retries to absorb infra jitter.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Capped rather than left at Playwright's default (roughly half the machine's CPU count --
  // 16 on this box's 32 cores), discovered empirically writing Task 15 Batch A's live-stack
  // specs: `webServer.command` below is `next dev` (Turbopack dev mode, not a production
  // build/start) -- a single such process serializes/slows badly under that many concurrent
  // browser contexts, and independently, GatewayService's own real per-client-IP "general"
  // rate limiter (backend/GatewayService/appsettings.json's `RateLimiting:General`, 100 req/60s
  // -- every spec's own Node-side Gateway fixture calls AND the dev server's server-side
  // fetches share one client IP) starts returning 429s once enough specs' combined request
  // volume lands in the same window at full parallelism. 4 workers reproducibly ran every Task
  // 15 Batch A spec clean; 16 (the unconstrained default) intermittently failed toolbar-driven
  // navigations with both symptoms above.
  //
  // Forced down to 1 as of Task 15 Batch C: GatewayService ALSO enforces a MUCH stricter
  // "strict" policy (`RateLimiting:Mutating`, PermitLimit 10/60s, a FIXED window) on every
  // mutating auction/bid route (create/update/delete auction, upload-url, thumbnail, place
  // bid) -- and Batch C's create/edit/delete/bid/real-time/image-upload specs collectively
  // issue far more than 10 such requests across a full run. `e2e/fixtures/mutation-
  // throttle.ts`'s `throttleMutation()` paces every one of THIS run's own mutating calls to
  // stay under that budget, but it's only correct as a single in-process counter -- parallel
  // workers are separate Node processes with independent budgets that could still collectively
  // burst past the server's real limit. Revisit both this and the `webServer` note above once
  // Phase 10 backs this with a `next build && next start` webServer instead.
  workers: 1,
  // Backend-dependent specs (auth, bidding, SignalR) can be slower than Playwright's own 30s
  // default once real network round-trips to the Gateway/IdentityService are involved (Task
  // 15.6+) -- widened further for Task 15 Batch C: `throttleMutation()` (see `workers` above)
  // can itself sleep up to just past a full 60s rate-limit window before a mutating request it's
  // pacing is allowed through, on top of whatever that request's own round-trip then takes.
  timeout: 90_000,
  expect: {
    timeout: 5_000,
  },

  reporter: [
    // gitignored (see .gitignore) -- regenerated per run, never committed.
    ["html", { outputFolder: "playwright-report", open: "never" }],
    ["list"],
  ],
  // Also gitignored -- traces/screenshots/videos captured per failing test.
  outputDir: "test-results",

  use: {
    baseURL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    // Task 15.6+ (auth specs) navigate to IdentityServer directly
    // (https://localhost:5001), which serves ASP.NET Core's self-signed dev
    // HTTPS certificate -- without this, every such navigation fails with
    // ERR_CERT_AUTHORITY_INVALID. Dev-only stack; same tradeoff
    // lib/dev-tls.ts documents for next-auth's own server-side token
    // exchange.
    ignoreHTTPSErrors: true,
  },

  // Chromium only for now (Task 14 scope) -- add `webkit`/`firefox` projects
  // here once cross-browser coverage is actually needed; each just needs
  // its own `npx playwright install <browser>` first.
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],

  // Starts `npm run dev` automatically when nothing is already listening on
  // baseURL -- but in this environment (and most local dev loops) a dev
  // server is already running on :3000, so `reuseExistingServer: true`
  // attaches to it instead of spawning a second one. CI should NOT reuse a
  // stray server (`reuseExistingServer` is forced off there) so a fresh,
  // known-good build always backs the test run.
  webServer: {
    command: "npm run dev",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
