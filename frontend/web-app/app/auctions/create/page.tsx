import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { AuctionForm } from "@/components/AuctionForm";
import { getDurationLimits } from "@/lib/auction-service";
import { hasAdminRole } from "@/lib/roles";
import type { DurationLimits } from "@/types/admin";

export const metadata: Metadata = {
  title: "Create auction | ApexAutoBid",
};

/**
 * Auction create page (Task 6). A **static** segment
 * (`app/auctions/create/page.tsx`), which Next.js's App Router always
 * matches before the sibling **dynamic** segment
 * `app/auctions/[id]/page.tsx` -- a request for `/auctions/create` never
 * reaches the `[id]` route (and so never tries to fetch an auction with id
 * "create"). See `app/auctions/[id]/edit/page.tsx` for the edit-mode
 * counterpart.
 *
 * Server Component: only the auth gate runs here, then hands off to the
 * Client Component form. Verified-email enforcement is real on the backend
 * (`POST api/auctions`'s "EmailVerified" policy) -- this page only checks
 * that a session exists at all; an unverified user still reaches the form
 * and finds out from the surfaced 403 on submit (`lib/auction-actions.ts`).
 */
export default async function CreateAuctionPage() {
  const session = await auth();

  if (!session?.user?.username || session.error === "RefreshTokenError") {
    // Sends the browser to IdentityServer's login page and back to this
    // exact route afterwards. Calling signIn() during render is illegal --
    // it writes the PKCE/state cookies, and cookies can only be modified in
    // a Server Action or Route Handler -- so the flow starts in the
    // app/auth/signin Route Handler instead.
    redirect("/auth/signin?callbackUrl=%2Fauctions%2Fcreate");
  }

  const isAdmin = hasAdminRole(session.user.role);

  // Anonymous, and never load-bearing to the page itself -- degrades to no client-side
  // constraint (the backend still enforces its own bounds either way) rather than failing the
  // whole page if the Auction Service is briefly unreachable.
  let durationLimits: DurationLimits | undefined;
  try {
    durationLimits = await getDurationLimits();
  } catch {
    durationLimits = undefined;
  }

  return (
    <div className="mx-auto w-full max-w-2xl space-y-6 px-4 py-8 sm:px-6">
      <div>
        <h1 className="text-3xl font-bold text-slate-900">Create auction</h1>
        <p className="text-sm text-slate-500">
          List a car for auction -- add its details, photos, and an end date.
        </p>
      </div>

      {session?.user?.isEmailVerified === false && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3">
          <p className="text-sm font-medium text-amber-800">
            Verify your email address before creating an auction -- check your inbox for the confirmation
            link.
          </p>
        </div>
      )}

      <AuctionForm mode="create" isAdmin={isAdmin} durationLimits={durationLimits} />
    </div>
  );
}
