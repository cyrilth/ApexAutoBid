"use client";

import { useEffect } from "react";
import Link from "next/link";
import { Button } from "flowbite-react";
import { CarIcon } from "@/components/icons/CarIcon";

/**
 * Route-level error boundary (Task 18, Docs/Requirements.md §13.2). Wraps
 * every route segment under `app/` (there's no more specific `error.tsx`
 * anywhere deeper today, so this is also the boundary that
 * `app/auctions/[id]/page.tsx`'s `getAuctionById` lands on when it throws
 * on a non-404 failure -- see `lib/auction-service.ts`'s remarks -- rather
 * than that page crashing to a raw 500).
 *
 * Must be a Client Component (Next requirement for error boundaries).
 * Deliberately generic copy only -- `error.message`/`error.stack` are never
 * rendered, only `console.error`'d for local/dev observability (§13.2:
 * "never render detail or stack traces to users in production"; production
 * builds strip the real message from Server Component errors before they
 * even reach this component -- Next's own `error.js` docs -- so there'd be
 * nothing safe to show anyway).
 */
export default function ErrorBoundary({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    // Dev observability only -- never surfaced to the user.
    console.error(error);
  }, [error]);

  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-8 sm:px-6">
      {/* Same visual shape as components/EmptyState.tsx (Docs/DesignGuide.md
          §6's empty-state pattern -- centered icon + one-line message + CTA)
          but not that component itself: this needs a second, non-link
          action (`reset()`), which EmptyState's props don't support. */}
      <div className="flex flex-col items-center gap-3 rounded-lg border border-slate-200 bg-white py-16 text-center">
        <CarIcon className="h-12 w-12 text-slate-300" />
        <h1 className="text-lg font-semibold text-slate-900">Something went wrong</h1>
        <p className="max-w-md text-sm text-slate-500">
          We hit a snag loading this page. Try again, or head back to the auction listing.
        </p>
        <div className="flex gap-3">
          <Button color="primary" onClick={() => reset()}>
            Try again
          </Button>
          <Button as={Link} href="/" color="light">
            Back to auctions
          </Button>
        </div>
      </div>
    </div>
  );
}
