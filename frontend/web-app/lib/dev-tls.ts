/**
 * Dev-only workaround for the ASP.NET Core dev HTTPS certificate.
 *
 * IdentityService's local `https://localhost:5001` listener uses the
 * `dotnet dev-certs https` self-signed certificate. Browsers happily talk to
 * it once you click through (or run `dotnet dev-certs https --trust`), but
 * Node's TLS stack -- which is what actually performs next-auth's
 * server-side authorization-code exchange against `/connect/token` (see
 * `auth.ts`) -- rejects it by default with `UNABLE_TO_VERIFY_LEAF_SIGNATURE`.
 *
 * `allowInsecureDevTlsIfConfigured` relaxes certificate verification for the
 * whole Node process via `NODE_TLS_REJECT_UNAUTHORIZED`, which is the
 * simplest fix available without pulling in a TLS-agent-aware fetch
 * implementation (Node's built-in `fetch` doesn't accept a custom `agent`,
 * and this project has no reason to add the `undici` package just for a dev
 * cert). That bluntness is intentionally fenced in on two sides so it can
 * never leak into a real deployment:
 *
 *   1. Hard-coded `NODE_ENV !== "production"` check -- not itself driven by
 *      an env var, so it cannot be flipped on by misconfiguring one.
 *   2. Requires the explicit opt-in `AUTH_IDENTITYSERVER_ALLOW_INSECURE_TLS=
 *      true` env var, so a default `npm run dev` (e.g. against a properly
 *      trusted cert, or a containerized IdentityService reached over plain
 *      HTTP inside a Docker network) is not silently downgraded either.
 *
 * Preferred alternative for anyone who'd rather not use this at all: run
 * `dotnet dev-certs https --trust` once so the OS trust store (and
 * therefore Node) trusts the dev certificate directly, and leave
 * `AUTH_IDENTITYSERVER_ALLOW_INSECURE_TLS` unset.
 */
export function allowInsecureDevTlsIfConfigured(): void {
  if (process.env.NODE_ENV === "production") return;
  if (process.env.AUTH_IDENTITYSERVER_ALLOW_INSECURE_TLS !== "true") return;

  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}
