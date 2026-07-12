import "server-only";

/**
 * The app's own public origin -- needed anywhere an absolute URL must be
 * resolvable outside the browser: Open Graph/Twitter Card metadata (Task
 * 5.6) and the canonical link handed to the social share buttons (Task
 * 5.5). Mirrors auth.ts's identical `AUTH_URL` fallback (used there for
 * IdentityServer's RP-initiated logout redirect) -- that's already the
 * app's documented public-origin source of truth (Docs/Requirements.md §6 /
 * `.env`), so this just reuses it rather than introducing a second env var
 * for the same value.
 */
export function getSiteUrl(): string {
  return process.env.AUTH_URL ?? "http://localhost:3000";
}
