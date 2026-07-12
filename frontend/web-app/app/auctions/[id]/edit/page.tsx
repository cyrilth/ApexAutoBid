import type { Metadata } from "next";
import { notFound, redirect } from "next/navigation";
import { auth } from "@/auth";
import { AuctionForm } from "@/components/AuctionForm";
import { getAuctionById } from "@/lib/auction-service";
import { hasAdminRole } from "@/lib/roles";

interface EditAuctionPageProps {
  params: Promise<{ id: string }>;
}

export async function generateMetadata({ params }: EditAuctionPageProps): Promise<Metadata> {
  const { id } = await params;
  const auction = await getAuctionById(id);
  return { title: auction ? `Edit ${auction.year} ${auction.make} ${auction.model} | ApexAutoBid` : "Edit auction" };
}

/**
 * Auction edit page (Task 6). Nested one level below the auction detail
 * route (`app/auctions/[id]/edit/page.tsx`), so it inherits the same `[id]`
 * dynamic segment without competing with `app/auctions/create/page.tsx`'s
 * static sibling (see that page's routing note).
 *
 * Two authorization layers, same as the backend's `UpdateAuction`
 * (AuctionsController remarks): sign-in is required to view the form at
 * all, and only the auction's own seller (or an admin) may actually see the
 * pre-filled form -- everyone else gets a friendly "not allowed" message
 * instead of a form that would just 403 on submit. `PUT api/auctions/{id}`
 * is still the real authority on both checks (`lib/auction-actions.ts`
 * surfaces its 403/404 inline if either ever changes between this render
 * and the submit).
 */
export default async function EditAuctionPage({ params }: EditAuctionPageProps) {
  const { id } = await params;
  const session = await auth();

  if (!session?.user?.username || session.error === "RefreshTokenError") {
    // signIn() can't run during render (cookie writes) -- see the
    // app/auth/signin Route Handler, which starts the flow legally.
    redirect(`/auth/signin?callbackUrl=${encodeURIComponent(`/auctions/${id}/edit`)}`);
  }

  const auction = await getAuctionById(id);
  if (!auction) {
    notFound();
  }

  const isOwner = session!.user.username === auction.seller;
  const isAdmin = hasAdminRole(session!.user.role);

  if (!isOwner && !isAdmin) {
    return (
      <div className="mx-auto w-full max-w-2xl space-y-4 px-4 py-8 sm:px-6">
        <h1 className="text-3xl font-bold text-slate-900">Edit auction</h1>
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
          <p className="text-sm font-semibold text-red-700">Not allowed</p>
          <p className="text-sm text-red-600">You can only edit your own auctions.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-2xl space-y-6 px-4 py-8 sm:px-6">
      <div>
        <h1 className="text-3xl font-bold text-slate-900">Edit auction</h1>
        <p className="text-sm text-slate-500">
          {auction.year} {auction.make} {auction.model}
        </p>
      </div>

      {session!.user.isEmailVerified === false && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3">
          <p className="text-sm font-medium text-amber-800">
            Verify your email address before saving changes -- check your inbox for the confirmation link.
          </p>
        </div>
      )}

      <AuctionForm mode="edit" auction={auction} />
    </div>
  );
}
