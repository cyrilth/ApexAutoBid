import { Suspense } from "react";
import Link from "next/link";
import { AuctionToolbar } from "@/components/AuctionToolbar";
import { AuctionResults } from "@/components/AuctionResults";
import { AuctionGridSkeleton } from "@/components/AuctionGridSkeleton";
import { parseAuctionSearchParams } from "@/lib/auction-search-params";

/**
 * Auction listing page (Phase 7 Task 4). Server Component -- `searchParams`
 * is the single source of truth for search/filter/sort/pagination state, so
 * results stay shareable/bookmarkable/back-button friendly (no client-only
 * state; Zustand lands in Task 9 for other concerns).
 */
export default async function Home({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = parseAuctionSearchParams(await searchParams);
  // Forces AuctionToolbar/AuctionResults to remount (fresh skeleton, fresh
  // uncontrolled field defaults) whenever the resolved query changes.
  const queryKey = JSON.stringify(query);

  return (
    <div className="mx-auto w-full max-w-7xl space-y-6 px-4 py-8 sm:px-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900">Auctions</h1>
          <p className="text-sm text-slate-500">
            Browse live, ending-soon, and finished car auctions.
          </p>
        </div>
        {/* Temporary until the real nav/auth menu lands (later Phase 7 tasks) --
            see app/session/page.tsx for the auth verification harness. */}
        <div className="flex items-center gap-4">
          {/* Styled Link rather than Flowbite `Button as={Link}`: Button is a
              Client Component, and a Server Component can't pass the Link
              function component across that boundary (RSC serialization). */}
          <Link
            href="/auctions/create"
            className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white hover:bg-primary-700 focus:outline-none focus:ring-4 focus:ring-primary-400"
          >
            Create auction
          </Link>
          <Link
            href="/session"
            className="rounded text-sm font-medium text-primary-700 hover:underline focus:outline-none focus:ring-2 focus:ring-primary-400"
          >
            Sign in / session
          </Link>
        </div>
      </div>

      <AuctionToolbar key={queryKey} query={query} />

      <Suspense key={queryKey} fallback={<AuctionGridSkeleton />}>
        <AuctionResults query={query} />
      </Suspense>
    </div>
  );
}
