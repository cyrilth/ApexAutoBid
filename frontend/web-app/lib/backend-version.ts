import "server-only";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * Backend (platform-wide) version, from the gateway's own `GET api/version`
 * (Docs/Versioning.md §5 -- "handled by the gateway itself, not proxied").
 *
 * Called from `components/Footer.tsx`, which renders in `app/layout.tsx` on
 * every single page (including every route's `error.tsx` boundary) -- so
 * this must NEVER throw. Any failure (gateway down, network error, bad
 * body) degrades to `"unavailable"` rather than taking the whole page down
 * with it.
 *
 * `revalidate: 300` (Task 16 brief) rather than `cache: "no-store"` -- the
 * version string changes only on a deploy, not per-request, so treating it
 * like any other slow-changing GET is exactly what Next's fetch data cache
 * is for; every render within the 5-minute window is served from cache
 * instead of hitting the gateway again.
 */
export async function getBackendVersion(): Promise<string> {
  try {
    const res = await fetch(`${GATEWAY_URL}/api/version`, {
      next: { revalidate: 300 },
    });

    if (!res.ok) return "unavailable";

    const body = (await res.json()) as { version?: string };
    return typeof body.version === "string" && body.version.length > 0 ? body.version : "unavailable";
  } catch {
    return "unavailable";
  }
}
