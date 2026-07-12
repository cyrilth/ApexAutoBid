/**
 * Minimal, unverified decode of a JWT's payload segment.
 *
 * This is deliberately NOT a verifying JWT library call -- we only ever use
 * it on an access token we just received directly from IdentityServer over
 * a TLS-protected server-to-server token exchange (auth.ts's `jwt`
 * callback), purely to lift the `username`/`role`/`email_verified` claims
 * (see backend/IdentityService/Config.cs `ApiResources`) into the next-auth
 * session for display/authorization-hint purposes. The access token itself
 * is what actually gets sent to the Gateway on every API call, where it is
 * independently and fully validated by each backend service's JwtBearer
 * middleware -- decoding it here again with signature verification would be
 * redundant, not safer.
 */
export function decodeJwtClaims(token: string | undefined): Record<string, unknown> | undefined {
  if (!token) return undefined;

  const payload = token.split(".")[1];
  if (!payload) return undefined;

  try {
    const base64 = payload.replaceAll("-", "+").replaceAll("_", "/");
    const json = Buffer.from(base64, "base64").toString("utf8");
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return undefined;
  }
}
