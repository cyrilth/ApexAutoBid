import { getAccessToken } from "@/lib/auth-token";

/**
 * Shared result envelope + helpers for every Server Action module
 * (lib/auction-actions.ts, lib/bid-actions.ts). Lives in its own plain module
 * because a "use server" file may only EXPORT async Server Actions -- but it
 * can freely IMPORT anything, so this is the single source of truth both
 * action modules pull from instead of each keeping a hand-synced copy.
 *
 * Transitively server-only: `lib/auth-token.ts` (imported below) carries
 * `import "server-only"`, so pulling this module into a Client Component is a
 * build error -- exactly the guarantee the action modules already had.
 */

/**
 * Typed result envelope for every Server Action -- never throws a raw fetch
 * error at the client (Docs/Requirements.md §13.2). Callers branch on
 * `success` and surface `error.title` as a red toast (title only, Task 19)
 * and/or the curated `error.detail` inline.
 */
export type ActionResult<T> =
  | { success: true; data: T }
  | { success: false; error: { title: string; detail?: string; status: number } };

/**
 * Every mutating action needs the signed-in user's bearer token
 * (lib/auth-token.ts is server-only, matching the "use server" action modules
 * that call this). `null` means the caller is signed out (or their refresh
 * failed); actions treat that the same way the backend would treat a missing
 * token: reject before calling the Gateway at all.
 */
export async function requireBearerToken(): Promise<string | null> {
  const token = await getAccessToken();
  return token ?? null;
}

export function signInRequiredError(action: string): ActionResult<never> {
  return {
    success: false,
    error: {
      title: "Sign-in required",
      detail: `Please sign in again to ${action}.`,
      status: 401,
    },
  };
}

/**
 * Parses a ProblemDetails body (RFC 7807, Docs/Requirements.md §13.1) when
 * present. Some 403s from [Authorize(Policy = "EmailVerified")] are a bare
 * framework `Forbid()` with no body at all (see AuctionsController's
 * remarks) -- `res.json()` throws on those, so this falls back to a generic,
 * status-appropriate title/detail rather than surfacing a parse error.
 */
export async function problemDetailsError(
  res: Response,
  fallbackTitle: string
): Promise<ActionResult<never>> {
  let title = fallbackTitle;
  let detail: string | undefined;

  try {
    const body = (await res.json()) as { title?: string; detail?: string };
    if (typeof body.title === "string") title = body.title;
    if (typeof body.detail === "string") detail = body.detail;
  } catch {
    if (res.status === 403) {
      title = "Not allowed";
      detail = "You don't have permission to do that.";
    }
  }

  return { success: false, error: { title, detail, status: res.status } };
}
