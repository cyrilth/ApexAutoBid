import { NextRequest } from "next/server";
import { signIn } from "@/auth";

/**
 * Starts the IdentityServer sign-in flow from a plain GET navigation.
 *
 * Server Components (e.g. the create/edit auction pages' auth gates) cannot
 * call `signIn()` during render -- it writes the PKCE/state cookies, and
 * "Cookies can only be modified in a Server Action or Route Handler". They
 * `redirect()` here instead, and this Route Handler (where cookie writes ARE
 * allowed) hands the browser to IdentityServer, which returns the user to
 * `callbackUrl` after login.
 */
export async function GET(request: NextRequest) {
  const requested = request.nextUrl.searchParams.get("callbackUrl") ?? "/";
  // Local-path check so this can't be abused as an open redirect.
  const callbackUrl = requested.startsWith("/") && !requested.startsWith("//") ? requested : "/";

  await signIn("identityserver", { redirectTo: callbackUrl });
}
