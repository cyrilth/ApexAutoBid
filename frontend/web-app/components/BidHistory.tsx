"use client";

import { formatCurrency } from "@/lib/format";
import { BidStatusBadge } from "@/components/BidStatusBadge";
import { EmptyState } from "@/components/EmptyState";
import { useBidStore } from "@/components/BidStoreProvider";
import { useLiveBids } from "@/hooks/useLiveBids";

function formatBidTime(bidTime: string): string {
  return new Date(bidTime).toLocaleString("en-US", { dateStyle: "medium", timeStyle: "short" });
}

interface BidHistoryProps {
  /** This auction's id -- passed straight to `useLiveBids` to filter the platform-wide "BidPlaced" broadcast down to just this page's auction. */
  auctionId: string;
}

/**
 * Bid history list (Task 5.3, Docs/DesignGuide.md §7 -- "vertical list,
 * newest first"). Reads from the shared `BidStoreProvider` (Task 9) instead
 * of a `bids` prop, so a bid placed in `BidPanel` (Task 8.1) -- and, as of
 * Task 8.2, a bid placed by any OTHER connected bidder via SignalR --
 * appears at the top instantly, no page reload.
 *
 * Mounts `useLiveBids(auctionId)` (Task 8.2): the one place on this page
 * that actually subscribes to "BidPlaced" -- every newly-arrived live bid
 * also gets Docs/DesignGuide.md §8's brief accent-leaf background pulse,
 * driven by the store's `latestLiveBidId` and cleared via
 * `clearLatestLiveBid` once the CSS animation (`app/globals.css`'s
 * `.animate-bid-pulse`) finishes.
 *
 * Client Component (it wasn't before Task 9): reading a Zustand store needs
 * a hook, which needs the browser. The store is seeded server-side with the
 * exact same list this component used to receive as a prop
 * (`app/auctions/[id]/page.tsx`'s `getBidsForAuction` call, passed to
 * `BidStoreProvider`'s `initialBids`), so the first client render still
 * matches the server-rendered HTML -- no layout shift, no hydration
 * mismatch.
 */
export function BidHistory({ auctionId }: BidHistoryProps) {
  useLiveBids(auctionId);

  const bids = useBidStore((state) => state.bids);
  const latestLiveBidId = useBidStore((state) => state.latestLiveBidId);
  const clearLatestLiveBid = useBidStore((state) => state.clearLatestLiveBid);

  if (bids.length === 0) {
    return <EmptyState message="No bids yet -- be the first to bid." />;
  }

  // `GET api/bids/{auctionId}` already returns bids newest first
  // (BidsController's own contract), and `prependBid`/`addLiveBid` preserve
  // that order for anything added client-side -- sorted defensively here
  // too so this component's rendering order doesn't silently depend on
  // either producer never changing that.
  const sorted = [...bids].sort((a, b) => new Date(b.bidTime).getTime() - new Date(a.bidTime).getTime());

  return (
    <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white">
      {sorted.map((bid) => (
        <li
          key={bid.id}
          className={`flex items-center justify-between gap-3 px-4 py-3 ${
            bid.id === latestLiveBidId ? "animate-bid-pulse" : ""
          }`}
          // Clears the pulse target once its own animation finishes -- fires only for the row
          // that actually carries the animation class (the plain rows below never receive it,
          // since they have no `animation` running to end).
          onAnimationEnd={() => {
            if (bid.id === latestLiveBidId) clearLatestLiveBid();
          }}
        >
          <div>
            <p className="font-semibold text-slate-900">{bid.bidder}</p>
            <p className="text-sm text-slate-500">{formatBidTime(bid.bidTime)}</p>
          </div>
          <div className="flex items-center gap-2">
            <p className="font-semibold text-slate-900">{formatCurrency(bid.amount)}</p>
            <BidStatusBadge status={bid.bidStatus} />
          </div>
        </li>
      ))}
    </ul>
  );
}
