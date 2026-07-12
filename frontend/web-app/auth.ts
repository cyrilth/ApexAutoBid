import NextAuth from "next-auth";
import type { JWT } from "next-auth/jwt";
import DuendeIdentityServer6 from "next-auth/providers/duende-identity-server6";
import { allowInsecureDevTlsIfConfigured } from "@/lib/dev-tls";
import { decodeJwtClaims } from "@/lib/jwt-claims";

// Must run before any fetch this module (or next-auth internally) makes --
// see lib/dev-tls.ts for why this is safe to leave in non-dev code paths.
allowInsecureDevTlsIfConfigured();

/**
 * Non-standard custom claim carrying the ASP.NET Identity username, added to
 * the ACCESS token (not the id_token/userinfo response) by the
 * "apexautobid" API resource -- see backend/IdentityService/Config.cs
 * `UsernameClaimType`. Every backend service treats this as the caller's
 * identity, so the frontend mirrors the same constant rather than a string
 * literal.
 */
const USERNAME_CLAIM = "username";

/** The redirect URI IdentityService's "webapp" client has registered is
 * `http://localhost:3000/api/auth/callback/identityserver`, which fixes
 * this provider's id at "identityserver" (next-auth builds that URL from
 * `basePath + /callback/ + provider.id`). Don't rename this without also
 * updating backend/IdentityService/Config.cs's `RedirectUris`.
 */
const IDENTITY_SERVER_PROVIDER_ID = "identityserver";

export const identityServerIssuer =
  process.env.AUTH_IDENTITYSERVER_ISSUER ?? "https://localhost:5001";

const identityServerClientId = process.env.AUTH_IDENTITYSERVER_ID ?? "webapp";
const identityServerClientSecret = process.env.AUTH_IDENTITYSERVER_SECRET ?? "";

// Fail fast on a missing client secret -- but NOT during `next build`, where
// this module is loaded while collecting page data and runtime secrets are
// deliberately absent (the Docker image build excludes .env by design; the
// secret arrives via the container environment at run time). NEXT_PHASE is
// set by Next itself for the duration of the production build.
if (!identityServerClientSecret && process.env.NEXT_PHASE !== "phase-production-build") {
  throw new Error(
    "AUTH_IDENTITYSERVER_SECRET is not set. Copy .env.example to .env " +
      "(gitignored) and fill in the webapp client's secret from " +
      "backend/IdentityService/Config.cs."
  );
}

// Scopes requested from the "webapp" client -- must exactly match
// Config.cs's `AllowedScopes` for that client (openid, profile,
// offline_access, apexautobid).
const IDENTITY_SERVER_SCOPE = "openid profile offline_access apexautobid";

/** Where IdentityServer's RP-initiated logout (`/connect/endsession`) sends
 * the browser back to once the IdentityServer-side session has ended --
 * must match Config.cs's `PostLogoutRedirectUris` for the "webapp" client.
 */
export const identityServerPostLogoutRedirectUri =
  process.env.AUTH_URL ?? "http://localhost:3000";

/** How much earlier than the JWT's real expiry we proactively refresh, so a
 * request that lands right at the boundary doesn't race the expiry.
 */
const REFRESH_SKEW_MS = 60 * 1000;

function readEmailVerifiedClaim(value: unknown): boolean | undefined {
  if (typeof value === "boolean") return value;
  if (typeof value === "string") return value === "true";
  return undefined;
}

/**
 * Applies the `username`/`role`/`email_verified` access-token claims (see
 * `USERNAME_CLAIM` above) onto a JWT session token, falling back to
 * whatever was already there (used on refresh, where a claim might
 * legitimately be absent from the new access token if it didn't change).
 */
function applyAccessTokenClaims(token: JWT, accessToken: string | undefined): JWT {
  const claims = decodeJwtClaims(accessToken);
  if (!claims) return token;

  return {
    ...token,
    username: (claims[USERNAME_CLAIM] as string | undefined) ?? token.username,
    role: (claims.role as string | string[] | undefined) ?? token.role,
    isEmailVerified: readEmailVerifiedClaim(claims.email_verified) ?? token.isEmailVerified,
  };
}

/**
 * Exchanges the current (one-time-use) refresh token for a new access
 * token, per Config.cs's `RefreshTokenUsage.OneTimeOnly` on the "webapp"
 * client. Duende always issues a fresh refresh token handle alongside the
 * new access token and immediately invalidates the one that was just used,
 * so the new refresh token MUST be persisted back onto the session token --
 * reusing the old handle on the next refresh would fail as "already used".
 */
async function refreshAccessToken(token: JWT): Promise<JWT> {
  if (!token.refreshToken) {
    return { ...token, error: "RefreshTokenError" };
  }

  try {
    const response = await fetch(`${identityServerIssuer}/connect/token`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "refresh_token",
        client_id: identityServerClientId,
        client_secret: identityServerClientSecret!,
        refresh_token: token.refreshToken,
      }),
    });

    const tokens: {
      access_token?: string;
      expires_in?: number;
      refresh_token?: string;
      id_token?: string;
      error?: string;
    } = await response.json();

    if (!response.ok || !tokens.access_token) {
      throw new Error(tokens.error ?? `Refresh failed with status ${response.status}`);
    }

    const refreshed: JWT = {
      ...token,
      accessToken: tokens.access_token,
      accessTokenExpiresAt: Date.now() + (tokens.expires_in ?? 60) * 1000,
      // Rotation: always take the new handle. Only fall back to the old one
      // in the (spec-noncompliant) case a provider omits refresh_token on
      // refresh entirely.
      refreshToken: tokens.refresh_token ?? token.refreshToken,
      idToken: tokens.id_token ?? token.idToken,
      error: undefined,
    };

    return applyAccessTokenClaims(refreshed, tokens.access_token);
  } catch (error) {
    console.error("Failed to refresh IdentityServer access token", error);
    // Flag the session so the UI can force the user back through sign-in --
    // returning the stale token silently would let API calls fail with
    // confusing 401s instead.
    return { ...token, error: "RefreshTokenError" };
  }
}

export const { handlers, auth, signIn, signOut } = NextAuth({
  // This app is self-hosted (Docker/Nginx or Kubernetes, per
  // Docs/Requirements.md §6), never deployed on Vercel, so next-auth must be
  // told to trust the incoming Host header when building callback/redirect
  // URLs. AUTH_TRUST_HOST=true (see .env) does the same thing via env
  // inference; set explicitly here for clarity.
  trustHost: true,
  session: { strategy: "jwt" },
  providers: [
    DuendeIdentityServer6({
      // Overrides the built-in provider's default id
      // ("duende-identity-server6") -- see IDENTITY_SERVER_PROVIDER_ID.
      id: IDENTITY_SERVER_PROVIDER_ID,
      name: "ApexAutoBid Account",
      issuer: identityServerIssuer,
      clientId: identityServerClientId,
      clientSecret: identityServerClientSecret,
      authorization: { params: { scope: IDENTITY_SERVER_SCOPE } },
      checks: ["pkce", "state"],
    }),
  ],
  callbacks: {
    async jwt({ token, account }) {
      // `account` is only present on the initial sign-in request.
      if (account) {
        const withTokens: JWT = {
          ...token,
          accessToken: account.access_token,
          refreshToken: account.refresh_token,
          idToken: account.id_token,
          accessTokenExpiresAt: account.expires_at
            ? account.expires_at * 1000
            : Date.now() + 60 * 1000,
        };
        return applyAccessTokenClaims(withTokens, account.access_token);
      }

      if (
        token.accessTokenExpiresAt &&
        Date.now() < token.accessTokenExpiresAt - REFRESH_SKEW_MS
      ) {
        return token;
      }

      return refreshAccessToken(token);
    },
    async session({ session, token }) {
      // Deliberately NOT copying accessToken/refreshToken/idToken onto the
      // session -- this object is serialized to the browser (useSession(),
      // GET /api/auth/session). Server-side callers that need the raw
      // access token use lib/auth-token.ts instead, which reads it straight
      // out of the encrypted session cookie without ever handing it to the
      // client.
      session.user.username = token.username;
      session.user.role = token.role;
      session.user.isEmailVerified = token.isEmailVerified;
      session.error = token.error;
      return session;
    },
  },
});
