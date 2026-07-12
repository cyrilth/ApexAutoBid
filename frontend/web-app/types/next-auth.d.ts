import type { DefaultSession } from "next-auth";
import type { JWT as DefaultJWT } from "next-auth/jwt";

// Augments next-auth's built-in types with the IdentityServer-specific
// fields set by the callbacks in auth.ts. See Docs/Requirements.md §3.4 for
// where the username/email_verified/role claims come from on the backend.
declare module "next-auth" {
  interface Session extends DefaultSession {
    /** Set when the background access-token refresh (auth.ts) has given up
     * -- the UI should treat the user as effectively signed out and prompt
     * them to sign in again. */
    error?: "RefreshTokenError";
    user: DefaultSession["user"] & {
      /** ASP.NET Identity username -- the `Seller`/`Winner`/`Bidder` identity used across every backend service. */
      username?: string;
      role?: string | string[];
      isEmailVerified?: boolean;
    };
  }
}

declare module "next-auth/jwt" {
  interface JWT extends DefaultJWT {
    /**
     * IdentityServer access token. NEVER surfaced through the `session`
     * callback -- read it server-side via lib/auth-token.ts instead so it
     * never reaches the browser.
     */
    accessToken?: string;
    /** One-time-use refresh token handle (Config.cs `RefreshTokenUsage.OneTimeOnly`) -- rotates on every use. */
    refreshToken?: string;
    /** Needed as `id_token_hint` for RP-initiated logout (lib/auth-actions.ts). */
    idToken?: string;
    /** Epoch milliseconds when `accessToken` expires. */
    accessTokenExpiresAt?: number;
    username?: string;
    role?: string | string[];
    isEmailVerified?: boolean;
    error?: "RefreshTokenError";
  }
}
