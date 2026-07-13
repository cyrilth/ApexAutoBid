/**
 * Phase 8 Task 7 -- run this suite against the docker-compose stack
 * (docker/docker-compose.yml) without touching /etc/hosts:
 *
 *   PLAYWRIGHT_BASE_URL=https://app.apexautobid.local \
 *   AUTH_IDENTITYSERVER_ISSUER=https://id.apexautobid.local \
 *   npm run test:e2e
 *
 * The stack's four public dev domains (app./api./id./storage.apexautobid.local)
 * only resolve inside the Docker network (nginx aliases) or via /etc/hosts
 * entries. The BROWSER side needs no hosts file: Chromium's
 * --host-resolver-rules maps the whole *.apexautobid.local zone to loopback,
 * where the stack's Nginx listens on 443 (the dev CA it serves is absorbed by
 * the config's existing ignoreHTTPSErrors). Node-side fixtures are unaffected
 * -- they already default to published loopback ports (gateway 127.0.0.1:6001,
 * Mailpit :8025, MinIO :9000).
 *
 * Kept free of @playwright/test imports so playwright.config.ts (which must
 * not pull the test runner's fixture machinery at config-load time) and
 * global-setup.ts (which launches its own browser, bypassing the config's
 * launchOptions) can both share it.
 */

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

const baseHostname = new URL(baseURL).hostname;

/** True when the suite targets the docker-compose stack's Nginx dev domains. */
export const targetsDockerStack = baseHostname.endsWith(".apexautobid.local");

/**
 * Extra Chromium launch args. Empty for localhost runs -- the mapping is only
 * added when it's actually needed, so a normal dev-loop run is byte-identical
 * to before.
 */
export const chromiumLaunchArgs: string[] = targetsDockerStack
  ? ["--host-resolver-rules=MAP *.apexautobid.local 127.0.0.1"]
  : [];

/**
 * The object-storage origin auction image URLs are served from (the backend's
 * `Images:PublicBaseUrl`), for specs that assert where uploads landed. Follows
 * the target stack: MinIO directly in the dev loop, the Nginx storage domain in
 * the docker-compose stack. Overridable for any other topology.
 */
export const imageStorageOrigin =
  process.env.IMAGE_STORAGE_ORIGIN ??
  (targetsDockerStack ? "https://storage.apexautobid.local" : "http://localhost:9000");
