/**
 * `session.user.role` (types/next-auth.d.ts) mirrors the JWT `role` claim,
 * which ASP.NET Identity/Duende serialize as a single string for one role
 * or a string array for several -- ASP.NET Core's default JWT handling
 * doesn't guarantee an array even when a user has just one role, so both
 * shapes must be handled everywhere the frontend checks for `admin`
 * (Requirements.md §3.4/§10).
 */
export function hasAdminRole(role: string | string[] | undefined): boolean {
  if (!role) return false;
  return Array.isArray(role) ? role.includes("admin") : role === "admin";
}
