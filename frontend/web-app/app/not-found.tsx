import { EmptyState } from "@/components/EmptyState";

/**
 * 404 page (Task 18, Docs/Requirements.md §13.2). Renders whenever
 * `notFound()` is called within a route segment (today: only
 * `app/auctions/[id]/page.tsx`, when `getAuctionById` returns `null` for an
 * unknown id -- see that page's remarks) as well as for any URL that
 * matches no route at all (Next's `not-found.js` docs).
 *
 * Reuses `components/EmptyState.tsx` directly rather than hand-rolling the
 * same centered-icon-plus-message-plus-CTA markup again (Docs/DesignGuide.md
 * §6) -- `components/AuctionResults.tsx` already renders it the same way,
 * from a Server Component, with a `ctaHref`/`Button as={Link}` pair, so this
 * is a proven-safe usage in this codebase, not a new pattern.
 */
export default function NotFound() {
  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-8 sm:px-6">
      <EmptyState
        message="We couldn't find the page or auction you were looking for. It may have been removed, or the link might be incorrect."
        ctaLabel="Back to auctions"
        ctaHref="/"
      />
    </div>
  );
}
