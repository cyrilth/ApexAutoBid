import { handlers } from "@/auth";

// next-auth's catch-all Route Handler -- serves the whole /api/auth/*
// surface (signin/signout/callback/session/csrf/...). The "identityserver"
// provider id configured in auth.ts makes the callback for it
// /api/auth/callback/identityserver, which is the exact redirect URI
// registered for the "webapp" client in
// backend/IdentityService/Config.cs.
export const { GET, POST } = handlers;
