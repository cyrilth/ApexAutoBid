import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { hasAdminRole } from "@/lib/roles";
import { AdminSidebar } from "@/components/admin/AdminSidebar";

/**
 * Role gate for the whole `/admin/*` area (Task 8.1). Server-side, not just a hidden nav link:
 * every page under this layout runs through this check before it renders. `role` comes from
 * the IdentityServer access token's `role` claim, already surfaced onto the session by
 * `auth.ts`'s callbacks (see `types/next-auth.d.ts`) -- nothing new needed there.
 *
 *  - Anonymous / a session whose background token refresh has given up: sent to sign in,
 *    returning to the admin dashboard (`/admin`) afterwards -- a fixed callbackUrl, because a
 *    layout has no reliable server-side view of which child path was requested; landing on the
 *    dashboard one click from any admin page is fine for a UX nicety. (Same gate shape as
 *    `app/auctions/create/page.tsx`, which CAN use its own exact path.)
 *  - Signed in but not an admin: redirected home rather than shown a 403 page -- the "admin"
 *    nav link is also never shown to these users (see `app/page.tsx`), so reaching here at all
 *    means they typed the URL directly.
 *
 * Every `api/admin/*` call the pages/actions under this layout make is independently
 * authorized by the backend regardless (401 anonymous / 403 non-admin) -- this gate is a UX
 * nicety, not the real security boundary.
 */
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const session = await auth();

  if (!session?.user?.username || session.error === "RefreshTokenError") {
    redirect("/auth/signin?callbackUrl=%2Fadmin");
  }

  if (!hasAdminRole(session.user.role)) {
    redirect("/");
  }

  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-6 px-4 py-8 sm:px-6 lg:flex-row">
      <div className="lg:w-64 lg:flex-shrink-0">
        <AdminSidebar />
      </div>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}
