import { Suspense } from "react";
import Link from "next/link";
import { auth } from "@/auth";
import { AuctionToolbar } from "@/components/AuctionToolbar";
import { AuctionResults } from "@/components/AuctionResults";
import { AuctionGridSkeleton } from "@/components/AuctionGridSkeleton";
import { LiveBanners } from "@/components/LiveBanners";
import { getActiveBanners } from "@/lib/admin-banners-service";
import { parseAuctionSearchParams } from "@/lib/auction-search-params";
import { hasAdminRole } from "@/lib/roles";

const HOME_BANNER_SCOPES = ["Global", "HomePage"] as const;

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

  const session = await auth();
  const isAdmin = hasAdminRole(session?.user?.role);

  // Anonymous, and never load-bearing to the page itself (Task 8.6) -- degrades to no banner
  // rather than failing the whole page if the Auction Service is briefly unreachable.
  let banners: Awaited<ReturnType<typeof getActiveBanners>> = [];
  try {
    banners = await getActiveBanners();
  } catch {
    banners = [];
  }

  return (
    <div className="mx-auto w-full max-w-7xl space-y-6 px-4 py-8 sm:px-6">
      <LiveBanners initialBanners={banners} scopes={HOME_BANNER_SCOPES} />

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
          {isAdmin && (
            <Link
              href="/admin"
              className="rounded text-sm font-medium text-primary-700 hover:underline focus:outline-none focus:ring-2 focus:ring-primary-400"
            >
              Admin
            </Link>
          )}
          <Link
            href="/session"
            className="rounded text-sm font-medium text-primary-700 hover:underline focus:outline-none focus:ring-2 focus:ring-primary-400"
          >
            Sign in / session
          </Link>
        </div>
      </div>

      {/* Distinct key prefixes: these are siblings, and React requires keys
          to be unique within the same children array. */}
      <AuctionToolbar key={`toolbar-${queryKey}`} query={query} />

      <Suspense key={`results-${queryKey}`} fallback={<AuctionGridSkeleton />}>
        <AuctionResults query={query} />
      </Suspense>
    </div>
  );
}
