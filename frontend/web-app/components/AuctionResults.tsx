import { searchAuctions } from "@/lib/search-service";
import type { AuctionQuery } from "@/lib/auction-search-params";
import { AuctionCard } from "@/components/AuctionCard";
import { AuctionPagination } from "@/components/AuctionPagination";
import { EmptyState } from "@/components/EmptyState";

/**
 * Fetches and renders the current page of search results (Task 4.1). Split
 * out from `app/page.tsx` so it can sit in its own `<Suspense>` boundary --
 * the toolbar above it renders instantly while this streams in behind a
 * skeleton grid (Docs/DesignGuide.md §6 "Loading"; Docs/Requirements.md
 * §13.2 covers the full ProblemDetails-to-toast error handling that's a
 * later task -- this is the "sensible message" fallback called for here).
 */
export async function AuctionResults({ query }: { query: AuctionQuery }) {
  let result;
  try {
    result = await searchAuctions(query);
  } catch {
    return (
      <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-8 text-center">
        <p className="text-sm font-medium text-red-700">
          We couldn&apos;t load auctions right now. Please try again shortly.
        </p>
      </div>
    );
  }

  if (result.results.length === 0) {
    return (
      <EmptyState
        message="No auctions match your filters."
        ctaLabel="Clear filters"
        ctaHref="/"
      />
    );
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {result.results.map((item) => (
          <AuctionCard key={item.id} item={item} />
        ))}
      </div>

      {result.pageCount > 1 && <AuctionPagination query={query} totalPages={result.pageCount} />}
    </div>
  );
}
