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

  // A single flaky retry locally still fails fast; CI (once Phase 10 wires
  // this in) gets a couple of retries to absorb infra jitter.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Backend-dependent specs (auth, bidding, SignalR) can be slower than the
  // 30s Playwright default once real network round-trips to the Gateway/
  // IdentityService are involved (Task 15.6+).
  timeout: 30_000,
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
